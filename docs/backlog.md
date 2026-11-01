# Backlog

Work moved to after 1.0, each item with the reason it waits. Everything the phase 2 backlog listed was either done
in phase 3 or appears here. The known limitations that users can run into are documented in
[install.md](install.md#known-limitations); the checks that need a real host are in the
[1.0 verification](hosts.md#10-verification) of `hosts.md`. Pilot findings and their measurements are in
[pilot-2026-10.md](pilot-2026-10.md), Stop timings in [performance.md](performance.md).

## Hooks and CLI

- **A resident process for Stop.** A warm single-file Stop takes 12.22 s, 29.0% below the 0.2.0 baseline of
  2026-10-28 and 34.5% below a 0.2.0 re-measure in the same session; a cold one 17.53 s. About 10 to 11 s of the
  warm Stop is the one `dotnet format` run, which a resident process could avoid by keeping a loaded workspace.
  Reason to wait: the target of phase 3 (30% faster warm, under 30 s cold) is met on the same-session comparison;
  a resident process adds lifetime, locking and stale-state problems that need their own design. Decide from user
  reports of Stop time.
- **A turn with a compile error costs a second build** (about 2 s): a build without dependencies that fails is
  done once more with them, in case a referenced project's output was missing. Reason to wait: correct as is, and
  small next to the format run.
- **A base type added in another file of a partial type** is not seen by the base list comparison of
  `ExistingApi`, which compares the declarations of one file. A diagnostic on that type then stays existing API and
  is not sent back. Reason to wait: it errs on the safe side (nothing gets renamed), and partial types split across
  files with base lists in more than one file are rare.
- **`Compile Remove` is ignored** when `IsForeign` reads the static `Compile` items of a project: a linked file that
  the project removes again still counts as its own, so a log with results only in that file is not recognized as a
  referenced project's, and the rebuild that would produce the project's own log is skipped. Reason to wait: it needs
  a linked file that is also removed, which no sample has.
- **No integration test runs an include list longer than 30000 characters** through `dotnet format`; the batching
  is covered by unit tests and a manual probe on SDK 10.0.403 and 10.0.114. Reason to wait: needs a sample with
  thousands of files, which is slow for CI.
- **Repositories without FDG pay a small cost per hook**: the onboarding check reads every `project.assets.json`
  (about 3 ms for a 4 MB file, about 0.3 s for 100 such projects). Reason to wait: bounded and measured; caching
  needs an invalidation rule.
- **SessionStart gives no FDG context when its 10-second inspection runs out before any onboarded project is found**
  (the git file list or the project discovery is still running). It then shows only
  `FDG: session inspection timed out; some checks were skipped.` Reason to wait: telling which context applies needs
  the discovery that ran out of time; only very large repositories on a slow disk hit it.
- **A Stop build without dependencies trusts the existing output of referenced projects** when their changes do not
  show in the turn's git status: a pull, checkout or commit between turns, a commit made by someone else during the
  turn, a source directory named `artifacts`, `bin` or `obj` (taken for build output), or a `ProjectReference` in a
  `Directory.Build.props` that a nearer one imports. The build can then report against stale referenced output.
  Reason to wait: building every dependency each turn costs seconds on every Stop; `dotnet build` once brings the
  output up to date, and [install.md](install.md#known-limitations) says so.
- **A wrapper killed by the host can leave `dotnet-fdg` running**: the wrapper scripts start the tool as a child
  process rather than with `exec`, so when the host ends the hook on its timeout, the tool keeps running until its
  own timeout. Reason to wait: the tool's internal 60-second Stop timeout ends it below the host's 90 seconds, so it
  is not left running for long; `exec` would lose the wrapper's report of a tool that fails without output.

## Setup

The `.editorconfig` entries that `/fdg-setup` offers for IDE0280, IDE0250 and IDE0251 do not handle every layout.
The user confirms each proposal, so a wrong one is visible before it is written. Reason to wait for all three:
rare layouts, and a fix needs a design for keeping the entries in step with the profiles.

- A project at the repository root, or projects nested inside each other, get entries whose globs cover more than
  one project.
- A root `[*.cs]` entry also applies to projects that do not use the package. With `EnforceCodeStyleInBuild` on,
  such a project then reports these three in its build, which can fail it under `TreatWarningsAsErrors`.
- The entries go stale when a project's profile changes later; setup does not revisit them.
- An existing `.editorconfig` section that covers only part of a project is not detected as covering it, so setup
  proposes an entry that overlaps it.
- One entry whose glob spans projects with different profiles gets the severities of one profile for all of them.

## Rules and analyzers

- **The CA1710 option lives on FDG3048 only.** `dotnet_code_quality.CA1710.exclude_indirect_base_types = false` is
  written by FDG3048's `config`. If a future profile turned FDG3048 off while other CA1710 rules stay on, the option
  would vanish for them. Reason to wait: all 14 CA1710 rules have the same severities in every profile today; move
  the option to profile level when that changes.
- **FDGA003 and FDGA023 assume an indentation level of four columns** and their code fixes insert four spaces,
  whatever `indent_size` and `tab_width` say; only the line length check reads `tab_width`. A repository indented
  with two spaces gets reports on lists it chopped correctly. Reason to wait: the book's samples and the Roslyn
  defaults use four, and no noise sample uses another width.
- **CA1063 on a `Dispose()` that already existed counts as existing API**: when the turn adds `IDisposable` to a
  type that already had a `Dispose()` method, the CA1063 variants reported on that method, rather than on the type,
  are not sent back to the AI. Only the base list of the type is compared, not a member's. Reason to wait: it errs on
  the safe side (nothing is blocked wrongly), and the diagnostic still shows in the build.

## Generator

- **`check` depends on the SDK's internal layout** (`DotnetTools/dotnet-format` under the SDK directory). When the
  layout differs it names the path it expected and the `--sdk` option. Reason to wait: it is a maintainer tool, and
  the error says what to do.

## Hosts

- **Rider** is not verified: it is not installed on the verification machine. Reason to wait: needs a Rider
  installation. The package uses only standard analyzer and globalconfig support, so the IDE checks of the
  1.0 verification are the ones to repeat there.
- **The interactive host checks that 1.0.0 shipped without**: Codex `/hooks` listing the changed hooks (A4), Codex
  SessionStart after a compaction (A8), the `bash` hook line on macOS or Linux, and live analysis in Visual Studio
  and VS Code (C1, C2). Reason to wait: they need the TUI or an IDE; the headless parts passed, and the results table
  in [hosts.md](hosts.md#results) gives the evidence for each.
- **Agents suppress a naming diagnostic instead of renaming.** In every rc.1 run, when Stop blocked with CA1710 on a
  class the prompt had named, the agent added a justified `[SuppressMessage]` rather than renaming it. Reason to wait:
  keeping a name the user gave is a defensible reading, and the suppression is visible in the diff; if users report
  it as unwanted, the Stop message could ask the agent to suppress only when the user asked to keep the code as is.
