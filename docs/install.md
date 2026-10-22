# Installing Framework Design Guidelines

The plugin has two parts: the `framework-design-guidelines` plugin for Claude Code or Codex (skills and hooks), and
the `dotnet-fdg` .NET tool that the hooks run. Both are needed.

## Requirements

- .NET SDK 8.0.100 or later.
- Windows: Git for Windows (Claude Code runs the hooks in Git Bash) and, for Codex, PowerShell 7 (`pwsh`) on the
  PATH.

## 1. Install the tool

```
dotnet tool install -g dotnet-fdg
```

Update it with `dotnet tool update -g dotnet-fdg`. Without the tool, every hook shows
`FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg`.

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
- Codex: `$fdg-setup`

It adds the `FrameworkDesignGuidelines.Analyzers` package, gives each project a profile (`library`, `application`
or `test`) and builds once. It changes only working-tree files; review them with `git diff` and commit them yourself.

## Known limitations

- **Two AI sessions in one repository at the same time** see each other's edits as their own, so a Stop can format
  or report lines the other session wrote.
- **The AI committing in the middle of a turn**: Stop still formats and checks the files afterwards, so the commit
  holds the unformatted code and the fixes are left uncommitted in the working tree.
- **Extracting a class into a new file while the source file stays** reports the moved code as changed in the new
  file, so it is formatted and checked as new code.
- **An explicit `ArtifactsPath` or a custom `BaseIntermediateOutputPath`**: SessionStart looks for the build marker
  in the project's `obj/fdg/`, so it reports such a project as never built.
- **Stop time grows with the number of projects touched** in the turn, since each is built in turn; the Stop check
  gives up after 60 seconds (`FDG_HOOK_TIMEOUT_SECONDS`).
