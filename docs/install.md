# Installing Framework Design Guidelines

The plugin has two parts: the `framework-design-guidelines` plugin for Claude Code or Codex (skills and hooks), and
the `dotnet-fdg` .NET tool that the hooks run. Both are needed.

## Requirements

- .NET SDK 10.0.100 or later. CI runs the integration tests on 10.0.100, the minimum, and the whole suite on 10.0.402,
  which the pilot also used. With an older SDK, some rules do not run, and the session start check says so.
- Codex: Codex CLI 0.163.0 or later. The hooks need the plugin root that Codex puts in the hook environment
  (`PLUGIN_ROOT`), as 0.163.0 does; see [hosts.md](hosts.md).
- Windows: Git for Windows (Claude Code runs the hooks in Git Bash) and, for Codex, PowerShell 7 (`pwsh`) on the
  PATH.

The hooks run `dotnet build` and `dotnet format` in the repository at the end of every turn, and `/fdg-setup` and
`/fdg-fix` run them too. A build runs the repository's own MSBuild files, so use the plugin only in repositories
you trust, as you would before running `dotnet build` yourself.

To try an unreleased build, run `dotnet pack -c Release -o artifacts/packages` in a clone, use `artifacts/packages`
as the NuGet source of both packages, and add the marketplace from the local clone.

## 1. Install the tool

```
dotnet tool install -g dotnet-fdg
```

Restart Claude Code or Codex after the first install: a host started before it does not have the global tools
directory (`~/.dotnet/tools`) on its PATH. The hooks also look there, but the skills run `dotnet-fdg` (the same tool
as `dotnet fdg`), which needs it on the PATH.

Update it with `dotnet tool update -g dotnet-fdg`. Without the tool, every hook shows
`FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg`; when it is installed but does not
start or fails without output (for example one whose .NET runtime is missing), they show `FDG: dotnet-fdg failed: ...`
with the first line of its error.

A repository whose local tool manifest (`.config/dotnet-tools.json` or `dotnet-tools.json`, in the repository or
above it) lists `dotnet-fdg` gets that pinned version in the hooks, through `dotnet tool run`, instead of the global
tool. Update the pinned version with `dotnet tool update --local dotnet-fdg`. It has to be at least as new as the
analyzer package; see [Versioning and compatibility](releasing.md#versioning-and-compatibility).

## 2. Install the plugin

### Claude Code

```
claude plugin marketplace add tianvan/framework-design-guidelines
claude plugin install framework-design-guidelines@framework-design-guidelines
```

Or, inside Claude Code: `/plugin marketplace add tianvan/framework-design-guidelines`, then
`/plugin install framework-design-guidelines@framework-design-guidelines`.

### Codex

```
codex plugin marketplace add tianvan/framework-design-guidelines
codex plugin add framework-design-guidelines@framework-design-guidelines
```

Then start Codex, open `/hooks` and approve the three FDG hooks (SessionStart, UserPromptSubmit, Stop).
**Codex skips hooks that are not approved, silently.** Approve them again after every plugin update that changes
them; until then the old approval does not apply and the hooks do nothing. A Codex update can ask for approval
again too: Codex 0.164.0 did so for hooks approved under an earlier Codex. Codex shows "Hooks need review" at
start when it does.

**Upgrading from 0.2.0:** 1.0 changes the command line of all three Codex hooks (they now read the plugin root from
the environment, so a path with `$` or a backtick works), so Codex lists them as modified after the update. Open
`/hooks` and approve them once more, on every OS. Claude Code needs nothing.

## 3. Set up each repository

In the repository, run the setup skill:

- Claude Code: `/framework-design-guidelines:fdg-setup` (or `/fdg-setup` when no other command has that name)
- Codex: the skill is `framework-design-guidelines:fdg-setup`. `$fdg-setup` may work when no other skill has that
  name; this has not been verified.

It adds the `FrameworkDesignGuidelines.Analyzers` package, gives each project a profile (`library`, `application`
or `test`) and builds once. It changes only working-tree files; review them with `git diff` and commit them yourself.

It also offers to set the severities of IDE0280, IDE0250 and IDE0251 in the root `.editorconfig`, and writes them
only if you agree. `dotnet format`, which `/fdg-fix` and the Stop check run, applies the fixes of these three only
when an `.editorconfig` sets their severity; the package's own settings are not enough. Without the entries, the
three are reported but not fixed.

## Known limitations

- **Two AI sessions in one repository at the same time** see each other's edits as their own, so a Stop can format
  or report lines the other session wrote.
- **Files you edit yourself during a turn**, for example in an IDE, count as the AI's: Stop formats them and reports
  their diagnostics like the AI's changes.
- **Changes inside git submodules** are not checked: Stop looks only at the repository the session runs in.
- **The AI committing in the middle of a turn**: Stop still formats and checks the files changed in those commits,
  against their content at the start of the turn. The commit holds the unformatted code, and the fixes are left
  uncommitted in the working tree. Commits from before the turn that a pull, checkout or rebase brings in are not
  checked, and Stop says so; a commit counts as the turn's when its committer date is at or after the turn's start.
- **Extracting a class into a new file while the source file stays** reports the moved code as changed in the new
  file, so it is formatted and checked as new code.
- **An explicit `ArtifactsPath` or a custom `BaseIntermediateOutputPath`**: SessionStart looks for the build marker
  in the project's `obj/fdg/` and where `UseArtifactsOutput` puts it by default (`artifacts/obj/<project>/fdg/`
  beside `Directory.Build.props`), so it reports such a project as never built. When both places have a marker,
  the newer one counts, so switching `UseArtifactsOutput` on or off takes effect with the next build. The hooks
  read the restore's `project.assets.json` from the same two places to tell whether a project uses the package;
  elsewhere they go by the declared package references only.
- **A `nuget.config` in a directory whose path contains `;`**: NuGet restore fails there (measured with SDK
  10.0.403), so such a repository cannot restore or build at all, with or without FDG. Move or rename the directory.
- **A comma in the SARIF log path**: the compiler splits its log option on commas. When the Stop's state directory
  has a comma in its path, the build gets the path relative to the project instead, which drops a comma in the
  repository's own name. When the relative path still has one (a git directory or a worktree path with a comma, or
  a git directory on another drive), no SARIF log is written for that project: Stop says so and skips its
  diagnostics check, while formatting still runs.
- **A `global.json` that pins an SDK that is not installed** breaks the checks: the builds that Stop and setup run
  fail before any analyzer runs. Install that SDK or relax the pin (`rollForward`).
- **Stop time grows with the number of projects touched** in the turn, since each is built in turn; the Stop check
  gives up after 60 seconds (`FDG_HOOK_TIMEOUT_SECONDS`). A value above about 85 is capped by the host, which ends
  the Stop hook after 90 seconds.
- **Referenced projects changed outside the turn**: when the turn changes no file of a referenced project, Stop builds
  without dependencies and uses that project's existing output. Changes that a pull, checkout or commit between turns
  brought in, commits someone else makes during the turn, a source directory named `artifacts`, `bin` or `obj`, and
  a `ProjectReference` in a `Directory.Build.props` that a nearer one imports are not seen, so Stop can check against
  out-of-date output. Run `dotnet build` once to bring it up to date.
- **A very large repository on a slow disk**: SessionStart inspects for at most 10 seconds. When that runs out before
  it finds a project that uses the package, the session starts without the FDG context and shows
  `FDG: session inspection timed out; some checks were skipped.` The end-of-turn checks still run.
