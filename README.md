# Framework Design Guidelines for Claude Code and Codex

Keeps the C# that an AI coding agent writes in line with
*Framework Design Guidelines* (3rd edition) by Krzysztof Cwalina, Jeremy Barton and Brad Abrams.

Two NuGet packages provide the analyzer and the .NET tool:

- **[`FrameworkDesignGuidelines.Analyzers`](https://www.nuget.org/packages/FrameworkDesignGuidelines.Analyzers)**: a
  NuGet package that turns on the SDK analyzers that check the book's rules, with severities chosen per profile
  (`library`, `application` or `test`), plus this project's own analyzers for rules the SDK does not check. It works
  in any build and IDE, with or without an AI agent.
- **[`dotnet-fdg`](https://www.nuget.org/packages/dotnet-fdg)**: applies formatting and safe fixes, and adds
  project skills, rule references and hooks for Claude Code and Codex to the repositories you select.

It covers the whole book, chapters 2 to 9 and appendices A and D: 830 rules, every one checked against the book. 320
of them map to analyzers (176 to SDK analyzers, 144 to this project's own), some of them only in part; the other 510
are judgment rules, which the skills give to the agent and the reviewer. See
[coverage](https://github.com/tianvan/framework-design-guidelines/blob/main/docs/coverage.md).

This project is independent. It is not affiliated with, endorsed by or sponsored by the authors of the book,
Microsoft or Pearson. See [NOTICE](https://github.com/tianvan/framework-design-guidelines/blob/main/NOTICE).

## Quick start with Claude Code or Codex

Use Git, an SDK-style C# project and .NET SDK 10.0.100 or later. Windows users need Git for Windows. Codex on Windows
also uses PowerShell 7 (`pwsh`).

1. From the target repository, preview a project installation. Replace `<version>` with the release number you
   want. Choose `claude`, `codex`, or `both`.

   ```sh
   dotnet tool exec dotnet-fdg@<version> -- init --host codex --dry-run
   ```

2. Run the same command without `--dry-run` and confirm. Reopen the repository at its Git root in the selected host and trust
   the project. In Codex, review and approve the three FDG entries in `/hooks`.
3. Run `$fdg-setup` in Codex or `/fdg-setup` in Claude Code. The skill proposes the Analyzer version and profiles,
   asks for confirmation, then builds the projects. Review and commit the host files and MSBuild changes.

The integration lives in `.agents/` and `.codex/` for Codex, or `.claude/` for Claude Code. The selected
repository pins the CLI and carries its rule references. A member cloning the repository can reuse a matching tool
already on their machine. If it is missing, SessionStart shows the one-time `prepare` command. Hooks run only in
repositories with the project integration. Stop formats and applies safe fixes to **entire C# files touched this
turn** in the projects you onboarded.

See [installation and team workflow](docs/install.md) for the generated file list, offline behavior, and the
Codex linked-worktree limitation.

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

## Example

In a project with the `library` profile, the agent adds this class in a turn:

```csharp
using System.Collections.ObjectModel;

namespace Shipping;

/// <summary>The orders that leave on one truck.</summary>
public class OrderBatch : Collection<Order> {
    /// <summary>Gets the total weight of the orders in kilograms.</summary>
    public decimal GetTotalWeight() {
        decimal total = 0;
        foreach (Order order in this) { total += order.Weight; }
        return total;
    }
}
```

When the agent ends its turn, the Stop hook moves the braces to their own lines and expands the one-line block. One
diagnostic is left that it does not fix, because the fix renames a public type, so it sends the agent back with
this report (the output of a real run, with SDK 10.0.403):

```
FDG found 1 issue(s) in lines you changed this turn. Fix them before finishing.
src/Shipping/OrderBatch.cs:6 CA1710 Rename Shipping.OrderBatch to end in either 'Collection' or 'Dictionary', 'Set', 'Stack', 'Queue'
    public class OrderBatch : Collection<Order>
    FDG3058 DO add the suffix “Collection,” except for reusable, specialized data types such as “Queue” and “HashSet.”
    FDG3057 DO add the suffix “Dictionary.” Note that `IDictionary` is a specific type of collection, but this guideline takes precedence over the more general collections guideline that follows.
```

The agent renames the class and its file, and the next Stop has nothing to report. The file is now:

```csharp
using System.Collections.ObjectModel;

namespace Shipping;

/// <summary>The orders that leave on one truck.</summary>
public class OrderCollection : Collection<Order>
{
    /// <summary>Gets the total weight of the orders in kilograms.</summary>
    public decimal GetTotalWeight()
    {
        decimal total = 0;
        foreach (Order order in this)
        {
            total += order.Weight;
        }

        return total;
    }
}
```

What no analyzer checks is left to `fdg-write` while the agent writes and to `/fdg-review` afterwards. An example
of a review finding for this file:

```
src/Shipping/OrderCollection.cs:9 — FDG5023 CONSIDER using a property if the member represents a logical attribute of the type.
  The total weight is an attribute of the collection, computed in memory; a `TotalWeight` property would follow the rule.
```

## Skills

| Claude Code | Codex | What it does |
|---|---|---|
| `/fdg-setup` | `$fdg-setup` | Sets up the repository: package, profiles, first build, tool check. |
| `/fdg-review [<path>...]` | `$fdg-review` | Reviews C# against the rules that tools cannot check. It reports findings that quote each rule and changes no code. |
| `/fdg-fix [<path>...]` | `$fdg-fix` | Makes the given code pass every tool check: runs `dotnet-fdg fix`, builds, then fixes the remaining diagnostics project by project. |
| (automatic) | (automatic) | The agent can invoke `fdg-write` while editing C# in an onboarded project. |

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

The Analyzer also works in an IDE or build without an Agent integration. Add it to a `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <FrameworkDesignGuidelinesProfile>library</FrameworkDesignGuidelinesProfile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FrameworkDesignGuidelines.Analyzers" Version="2.0.0" PrivateAssets="all" />
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

Run `$fdg-fix` in Codex or `/fdg-fix` in Claude Code to use the repository's pinned tool and rules. To use the CLI
directly, run `dotnet tool exec dotnet-fdg@<version> -- fix [<path>...]`. Paths can be directories, `.csproj`,
`.sln`, `.slnx` or `.cs` files. The default is the current directory.

## Updating and removing

Run `init` with the new release number to update the tool pin, all installed host files and the shared references
in one change. Preview with `--dry-run`. The update reports locally edited files before it writes. Adjust the
Analyzer version through your project's usual NuGet configuration and include it in the same PR when upgrading
both. To roll back, revert that PR and use `prepare` if the previous tool is missing from the cache.

Run `dotnet tool exec dotnet-fdg@<installed-version> -- uninstall --host codex|claude|both` to remove the
integration. Choose one of the three host values. See [removal and rollback](docs/install.md#update-and-remove).

## Building from source

```
dotnet build -c Release
dotnet test -c Release
dotnet pack -c Release -o artifacts/packages
```

`artifacts/packages` then holds both packages. Use it as a NuGet source to try them before a release, for example
`dotnet tool exec --source /absolute/path/to/artifacts/packages dotnet-fdg@<version> -- --version`.

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
