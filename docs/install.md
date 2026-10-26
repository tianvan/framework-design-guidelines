# Installing Framework Design Guidelines

The plugin has two parts: the `framework-design-guidelines` plugin for Claude Code or Codex (skills and hooks), and
the `dotnet-fdg` .NET tool that the hooks run. Both are needed.

## Requirements

- .NET SDK 8.0.100 or later. That is the lowest version the session start check accepts, but it is untested: the
  pilot and CI use 10.0.402. A `.slnx` solution needs SDK 9.0.200 or later.
- Windows: Git for Windows (Claude Code runs the hooks in Git Bash) and, for Codex, PowerShell 7 (`pwsh`) on the
  PATH.

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
start, they show `FDG: dotnet-fdg failed to start: ...` with the first line of its error.

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
them; until then the old approval does not apply and the hooks do nothing.

## 3. Set up each repository

In the repository, run the setup skill:

- Claude Code: `/framework-design-guidelines:fdg-setup` (or `/fdg-setup` when no other command has that name)
- Codex: the skill is `framework-design-guidelines:fdg-setup`. `$fdg-setup` may work when no other skill has that
  name; this has not been verified.

It adds the `FrameworkDesignGuidelines.Analyzers` package, gives each project a profile (`library`, `application`
or `test`) and builds once. It changes only working-tree files; review them with `git diff` and commit them yourself.

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
  in the project's `obj/fdg/`, so it reports such a project as never built.
- **A `global.json` that pins an SDK that is not installed** breaks the checks: the builds that Stop and setup run
  fail before any analyzer runs. Install that SDK or relax the pin (`rollForward`).
- **Stop time grows with the number of projects touched** in the turn, since each is built in turn; the Stop check
  gives up after 60 seconds (`FDG_HOOK_TIMEOUT_SECONDS`). A value above about 85 is capped by the host, which ends
  the Stop hook after 90 seconds.
