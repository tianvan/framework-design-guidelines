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

3. Update the version in the README's package reference example, commit, and push to `main`. Wait for CI to pass.
4. Tag and push:

   ```
   git tag v0.1.0
   git push origin v0.1.0
   ```

5. Watch the run with `gh run watch`. After it succeeds, check that both packages appear on nuget.org (indexing
   can take a few minutes) and that `dotnet tool install -g dotnet-fdg` installs the new version.

A failed release can be re-run from the Actions page once the cause is fixed. If the fix needs a new commit, delete
the tag (`git push --delete origin vX.Y.Z`, `git tag -d vX.Y.Z`) and tag the new commit. A version that already
reached nuget.org cannot be replaced: publish the next version instead, and unlist the bad one on nuget.org if
needed.
