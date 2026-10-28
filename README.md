# Framework Design Guidelines for Claude Code and Codex

Keeps the C# that an AI coding agent writes in line with
*Framework Design Guidelines* (3rd edition) by Krzysztof Cwalina, Jeremy Barton and Brad Abrams.

It has three parts:

- **[`FrameworkDesignGuidelines.Analyzers`](https://www.nuget.org/packages/FrameworkDesignGuidelines.Analyzers)**: a
  NuGet package that turns on the SDK analyzers that check the book's rules, with severities chosen per profile
  (`library`, `application` or `test`), plus this project's own analyzers for rules the SDK does not check. It works
  in any build and IDE, with or without an AI agent.
- **[`dotnet-fdg`](https://www.nuget.org/packages/dotnet-fdg)**: a .NET tool. `dotnet fdg fix` applies the formatting
  and the fixes that do not change a public API. The plugin's hooks also run it.
- **The `framework-design-guidelines` plugin** for Claude Code and Codex:
  - skills that give the agent the book's rules that tools cannot check, quoted word for word with their ids;
  - a review skill;
  - hooks that format and check the lines the agent changed before it ends its turn.

> **Preview.** Version 0.x covers the whole book, chapters 2 to 9 and appendices A and D: 830 rules, every one checked
> against the book. See
> [coverage](https://github.com/tianvan/framework-design-guidelines/blob/main/docs/coverage.md).

This project is independent. It is not affiliated with, endorsed by or sponsored by the authors of the book,
Microsoft or Pearson. See [NOTICE](https://github.com/tianvan/framework-design-guidelines/blob/main/NOTICE).

## Requirements

- .NET SDK 8.0.100 or later (CI and the pilot used 10.0.402). A `.slnx` solution needs SDK 9.0.200 or later.
- SDK-style C# projects. Projects can still target .NET Framework or .NET Standard and use C# 7.3: the SDK version
  is independent of the target framework.
- For the plugin: Claude Code or Codex, and git. On Windows, Git for Windows (Claude Code runs the hooks in Git
  Bash). For Codex on Windows, also PowerShell 7 (`pwsh`) on the PATH.

## Quick start with Claude Code or Codex

1. Install the tool, then restart Claude Code or Codex so that `~/.dotnet/tools` is on its PATH:

   ```
   dotnet tool install -g dotnet-fdg
   ```

2. Install the plugin.

   Claude Code:

   ```
   claude plugin marketplace add tianvan/framework-design-guidelines
   claude plugin install framework-design-guidelines@framework-design-guidelines
   ```

   Codex:

   ```
   codex plugin marketplace add tianvan/framework-design-guidelines
   codex plugin add framework-design-guidelines@framework-design-guidelines
   ```

   Then start Codex, open `/hooks` and approve the three FDG hooks. **Codex silently skips hooks that are not
   approved.** Approve them again after a plugin update that changes them.

3. In each repository, run the setup skill: `/framework-design-guidelines:fdg-setup` in Claude Code (or `/fdg-setup`
   when no other command has that name), `framework-design-guidelines:fdg-setup` in Codex.

   It lists the files it will change and waits for you to confirm. Then it adds the analyzer package, gives each
   directory a profile and builds once. It only changes files in the working tree: review them with `git diff` and
   commit them yourself.

[docs/install.md](https://github.com/tianvan/framework-design-guidelines/blob/main/docs/install.md) has the details
and the known limitations.

## What happens while the agent works

- **Session start.** The hooks add a short note to the agent's context: which projects are set up and with which
  profile, the language version, and any setup problem, such as a missing `dotnet-fdg` or an SDK that is too old.
- **Writing code.** The `fdg-write` skill loads when the agent edits `.cs` files. It holds the judgment rules (for
  example how to name a type or when to use a property) quoted from the book with their ids, and points to reference
  files with the rest of each chapter and its examples.
- **End of turn (Stop hook).** For the files the agent changed in this turn, the hook:
  1. builds the projects that own them;
  2. formats the changed lines and applies the safe automatic fixes;
  3. sends the agent back to work, at most twice per turn, if the changed lines still have FDG, CA or IDE
     diagnostics. The report quotes the book's rule for each diagnostic.

  The hook checks only the lines changed in this turn, so existing code with many warnings does not flood the agent.
  It gives up after 60 seconds (`FDG_HOOK_TIMEOUT_SECONDS`).

## Skills

| Claude Code | Codex | What it does |
|---|---|---|
| `/fdg-setup` | `framework-design-guidelines:fdg-setup` | Sets up the repository: package, profiles, first build, tool check. |
| `/fdg-review [<path>...]` | `framework-design-guidelines:fdg-review` | Reviews C# against the rules that tools cannot check. It reports findings that quote each rule and changes no code. |
| `/fdg-fix [<path>...]` | `framework-design-guidelines:fdg-fix` | Makes the given code pass every tool check: runs `dotnet-fdg fix`, builds, then fixes the remaining diagnostics project by project. |
| (automatic) | (automatic) | `fdg-write` loads by itself when the agent writes C#. |

In Claude Code, prefix the commands with `framework-design-guidelines:` if another plugin uses the same name.

## Profiles

Each project selects a rule set with the MSBuild property `FrameworkDesignGuidelinesProfile`:

| Profile | For | Effect |
|---|---|---|
| `library` | Libraries published for others to use | Strictest: API design rules for the public surface, plus versioning and compatibility rules |
| `application` | Application code, including every layer of a layered or DDD design | API design rules for all types and members, no binary compatibility checks |
| `test` | Test projects | Chapter 3 rules are off (`Method_WhenX_DoesY` is fine), API design rules off |

A project that references the package without a profile gets warning FDG0001 and no FDG rules. An unknown profile
gives FDG0002.

## Using the analyzer package without an agent

The package works without the plugin. Add it to a `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <FrameworkDesignGuidelinesProfile>library</FrameworkDesignGuidelinesProfile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FrameworkDesignGuidelines.Analyzers" Version="0.1.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Keep `PrivateAssets="all"`: the package is a development dependency, so its rules never flow to your library's
consumers. With central package management, put the version in `Directory.Packages.props` instead.

The package turns on `EnableNETAnalyzers` and `EnforceCodeStyleInBuild` unless you set them yourself. Your own
`.editorconfig` settings still apply: the profiles are global analyzer configs, which an `.editorconfig` overrides.
Each FDG diagnostic links to its rule page in
[docs/rules](https://github.com/tianvan/framework-design-guidelines/tree/main/docs/rules).

## Cleaning up existing code

`dotnet fdg fix` applies the formatting and the automatic fixes that do not change a public API:

```
dotnet fdg fix [<path>...]
```

Paths can be directories, `.csproj`, `.sln`, `.slnx` or `.cs` files. The default is the current directory. Fixes
that would rename or change a public API are never applied automatically.

To keep the history readable, you can run it once over the whole repository before you start, commit that change on
its own, and add the commit to `.git-blame-ignore-revs`:

```
dotnet fdg fix
git commit -am "style: apply dotnet fdg fix"
git rev-parse HEAD >> .git-blame-ignore-revs
git config blame.ignoreRevsFile .git-blame-ignore-revs
```

## Updating

```
dotnet tool update -g dotnet-fdg
claude plugin marketplace update framework-design-guidelines      # Claude Code
codex plugin marketplace upgrade framework-design-guidelines      # Codex
```

Update the `FrameworkDesignGuidelines.Analyzers` version in your projects to match the tool. In Codex, approve the
hooks again in `/hooks` if the update changed them.

## Building from source

```
dotnet build -c Release
dotnet test -c Release
dotnet pack -c Release -o artifacts/packages
```

`artifacts/packages` then holds both packages. Use it as a NuGet source to try them before a release, for example
`dotnet tool install -g dotnet-fdg --add-source artifacts/packages`.

The rule data in `rules/*.yaml` is the single source of the analyzer configuration, the rules manifest, the skill
references and the docs. After you change it, regenerate with `dotnet run --project src/Fdg.Generator -- generate`.
CI fails when the generated files are out of date.

Releases are published by GitHub Actions; see
[docs/releasing.md](https://github.com/tianvan/framework-design-guidelines/blob/main/docs/releasing.md).

## License

Code and content written for this project are under the
[MIT License](https://github.com/tianvan/framework-design-guidelines/blob/main/LICENSE). Excerpts from the book remain
the property of their rights holders; see
[NOTICE](https://github.com/tianvan/framework-design-guidelines/blob/main/NOTICE).
