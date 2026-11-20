# Releasing

Pushing a tag `vX.Y.Z` runs [`.github/workflows/release.yml`](../.github/workflows/release.yml):

1. the full CI (build, generated-file check, tests, rule mapping check) on Windows, Ubuntu and macOS;
2. a check that the tag matches every version in the repository (see below);
3. `dotnet pack -c Release`, which produces `FrameworkDesignGuidelines.Analyzers.X.Y.Z.nupkg` and
   `dotnet-fdg.X.Y.Z.nupkg`;
4. a push of both packages to nuget.org (`--skip-duplicate`, so a re-run after a partial failure is safe);
5. a GitHub release for the tag with both packages attached. Versions `0.x` and versions with a suffix such as
   `-preview.1` are marked as prereleases.

The `dotnet-fdg` NuGet package contains both hosts' project assets, including generated references. CI tests
installation, preparation and uninstall from the two built packages.

## One-time setup

Publishing uses [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): GitHub
gives the workflow a short-lived token, so no API key is stored in the repository.

1. On nuget.org, sign in, open your user name → **Trusted Publishing**, and add a policy:
   - Repository owner: `tianvan`
   - Repository: `framework-design-guidelines`
   - Workflow file: `release.yml`
   - Environment: `nuget` (optional; if you leave it empty, any job of `release.yml` may publish)
   - Select Scopes: **Push new packages and package versions** (the first release creates both package ids)
   - Select Packages: Glob Pattern `*`. Neither package exists yet, so there is nothing to tick in the package
     list. After the first release you can narrow it: tick `FrameworkDesignGuidelines.Analyzers` and `dotnet-fdg`
     instead of the pattern, and choose **Push only new package versions** (if the page does not let you change
     the scope, add a new policy with these settings and delete the old one).
2. In the GitHub repository, open **Settings → Secrets and variables → Actions → Variables** and add the repository
   variable `NUGET_USER` with your nuget.org user name (the profile name, not the email address).
3. Optional: **Settings → Environments → nuget** → add yourself as a required reviewer, so that each release waits
   for your approval before it pushes to nuget.org. The workflow creates the environment on its first run if it
   does not exist.

The first push creates both package ids under your nuget.org account.

## Cutting a release

1. Set `<Version>` in `Directory.Build.props`.
2. Regenerate, so `src/FrameworkDesignGuidelines.Package/data/rules.json` picks up the version, and run the tests:

   ```
   dotnet run --project src/Fdg.Generator -- generate
   dotnet test -c Release
   ```

3. Update the version in the README's package reference example and check the release's section in
   `CHANGELOG.md`, with the changes that can fail a `TreatWarningsAsErrors` build in their own subsection. For a
   prerelease tag such as `v1.0.0-rc.1` the section stays `Unreleased`; for the final release, replace
   `Unreleased` with the date. Commit, and push to `main`. Wait for CI to pass.
4. Tag and push:

   ```
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

5. Watch the run with `gh run watch`. After it succeeds, check that both packages appear on nuget.org (indexing
   can take a few minutes) and that `dotnet tool exec dotnet-fdg@X.Y.Z -- --version` resolves the new version.

A failed release can be re-run from the Actions page once the cause is fixed. If the fix needs a new commit, delete
the tag (`git push --delete origin vX.Y.Z`, `git tag -d vX.Y.Z`) and tag the new commit. A version that already
reached nuget.org cannot be replaced: publish the next version instead, and unlist the bad one on nuget.org if
needed.

## Versioning and compatibility

From 1.0.0 on, the version number tells you what an update can break. It covers the diagnostic ids, the
`dotnet-fdg` commands and options, the hook protocol between the host launchers and `dotnet-fdg`, the `rules.json` manifest
and build marker that the analyzer package writes for `dotnet-fdg`, the profiles, and the minimum SDK. The two
packages and the embedded host assets share one release version.

- **Ids**: FDG and FDGA ids are never reused and never renumbered. A rule that is removed keeps its id retired.
- **Major version** for:
  - removing a supported installation or distribution method;
  - removing a diagnostic id;
  - removing or renaming a `dotnet-fdg` command or option;
  - an incompatible change to the hook protocol (the `dotnet-fdg hook <event>` command line and the JSON it reads
    and writes) or to the `rules.json` and marker protocol;
  - raising the minimum SDK.
- **Minor version** for:
  - new diagnostics;
  - raising a severity;
  - changing a profile's defaults, such as turning a rule on or changing an analyzer option.

  Each of these can fail a build that uses `TreatWarningsAsErrors`, so [CHANGELOG.md](../CHANGELOG.md) lists them
  in a section of their own in every release.
- **Patch version** only for fewer false positives, crash fixes and corrected wording.

### Matching the Analyzer, tool and host assets

The Analyzer writes a protocol number into the build marker and its `data/rules.json`. The tool supports every
protocol up to `FixCommand.SupportedProtocol` (currently 1). SessionStart reports a newer protocol as a setup
issue. Stop and `fix` skip that project until its tool is updated to a compatible release.

The project installation records pin the CLI version. Launchers select that version from the local cache.
`init` updates the pins and the installed host assets together. Keep the bare `hook <event>` entry point for
existing command callers. The current launchers pass the host explicitly and validate the worktree's record.
