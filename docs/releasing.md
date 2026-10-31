# Releasing

Pushing a tag `vX.Y.Z` runs [`.github/workflows/release.yml`](../.github/workflows/release.yml):

1. the full CI (build, generated-file check, tests, rule mapping check, plugin validation) on Windows and Ubuntu;
2. a check that the tag matches every version in the repository (see below);
3. `dotnet pack -c Release`, which produces `FrameworkDesignGuidelines.Analyzers.X.Y.Z.nupkg` and
   `dotnet-fdg.X.Y.Z.nupkg`;
4. a push of both packages to nuget.org (`--skip-duplicate`, so a re-run after a partial failure is safe);
5. a GitHub release for the tag with both packages attached. Versions `0.x` and versions with a suffix such as
   `-preview.1` are marked as prereleases.

The plugin itself is not packaged: Claude Code and Codex install it from this repository's default branch through
`.claude-plugin/marketplace.json`. Tag a commit on `main`, so the plugin users get and the packages match.

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

1. Set the new version in all of these files (they must match, or the release fails before it publishes anything):
   - `Directory.Build.props`: `<Version>`
   - `.claude-plugin/marketplace.json`: both `version` fields
   - `plugins/framework-design-guidelines/.claude-plugin/plugin.json`: `version`
   - `plugins/framework-design-guidelines/.codex-plugin/plugin.json`: `version`
2. Regenerate, so `src/FrameworkDesignGuidelines.Package/data/rules.json` picks up the version, and run the tests:

   ```
   dotnet run --project src/Fdg.Generator -- generate
   dotnet test -c Release
   ```

3. Update the version in the README's package reference example, give the release's section in `CHANGELOG.md` its date
   (with the changes that can fail a `TreatWarningsAsErrors` build in their own subsection), commit, and push to
   `main`. Wait for CI to pass.
4. Tag and push:

   ```
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

5. Watch the run with `gh run watch`. After it succeeds, check that both packages appear on nuget.org (indexing
   can take a few minutes) and that `dotnet tool install -g dotnet-fdg` installs the new version.

A failed release can be re-run from the Actions page once the cause is fixed. If the fix needs a new commit, delete
the tag (`git push --delete origin vX.Y.Z`, `git tag -d vX.Y.Z`) and tag the new commit. A version that already
reached nuget.org cannot be replaced: publish the next version instead, and unlist the bad one on nuget.org if
needed.

## Versioning and compatibility

From 1.0.0 on, the version number tells you what an update can break. It covers the diagnostic ids, the
`dotnet-fdg` commands and options, the hook protocol between the plugin and `dotnet-fdg`, the `rules.json` manifest
and build marker that the analyzer package writes for `dotnet-fdg`, the profiles, and the minimum SDK. The two
packages and the plugin are released together with one version.

- **Ids**: FDG and FDGA ids are never reused and never renumbered. A rule that is removed keeps its id retired.
- **Major version** for:
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

### Matching the package, the tool and the plugin

The analyzer package writes a protocol number into each project's build marker (`obj/fdg/marker.json`) and into its
`data/rules.json`. `dotnet-fdg` supports every protocol up to its own (`FixCommand.SupportedProtocol`, currently 1)
and reads older ones unchanged. A project whose package has a newer protocol than the tool is not checked: session
start lists it as a setup issue, and Stop and `dotnet fdg fix` skip it with a message to run
`dotnet tool update -g dotnet-fdg`. Package and tool versions therefore do not have to be equal; a tool at least as
new as the package always works. Every release from 0.1.0 to 1.0.0 uses protocol 1, and 1.0.0 does not change its
meaning. A change that an older tool cannot read raises the protocol number, and is a major version.

The plugin does not check the tool's version. Its hooks run `dotnet-fdg hook session-start`, `turn-start` and
`turn-stop` (through the repository's local tool manifest when it lists `dotnet-fdg`). A tool that cannot run the
command, for example one too old to know `hook` or one without its runtime, exits without output, and the wrapper
then shows `FDG: dotnet-fdg failed:` with the first line of its error instead of failing silently. Keep the tool
updated with the plugin; the tested combination is the same version of all three.
