# Changelog

This file starts at 1.0.0. Earlier releases are described in their
[GitHub releases](https://github.com/tianvan/framework-design-guidelines/releases). The versioning rules are in
[docs/releasing.md](docs/releasing.md#versioning-and-compatibility).

## 1.0.0 - 2026-11-01

0.1.0 covered chapter 3 and appendix A of the book (158 rules); 0.2.0 extended the rule data, the analyzers and the
skill references to the whole book, chapters 2 to 9 and appendices A and D (830 rules, each checked against the
book). 1.0.0 adds no rules. It makes what the agent is told more accurate, makes the end-of-turn check faster and
sturdier, raises the minimum SDK to one that CI tests, and sets out the version and compatibility rules that later
releases follow.

### Changes that can fail a build with `TreatWarningsAsErrors`

- **CA1710 checks the whole type hierarchy** in the `library` and `application` profiles: the package now sets
  `dotnet_code_quality.CA1710.exclude_indirect_base_types = false`. A type that derives from a collection,
  dictionary, exception or attribute type through another base class, for example `class OrderBatch :
  Collection<Order>` or a class derived from your own exception class, is now reported when its name lacks the
  suffix. This affects every rule that maps to CA1710: FDG3048 to FDG3050, FDG3053, FDG3056 to FDG3059, FDG3074,
  FDG3076, FDG7057, FDG8005, FDG8038 and FDG8039. The `test` profile has CA1710 off, as before.
- **FDGA023 counts a tab as reaching the next tab stop** of `tab_width` (4 when not set) when it measures a line
  against `max_line_length`; before, a tab counted as one column. In files indented with tabs, parameter and
  argument lists on lines that now measure longer than the limit are reported. In the other direction, a list item
  that starts with a block comment (`/* name */ value`) on its own line now counts as chopped and is no longer
  reported.
- **The `.editorconfig` entries that `/fdg-setup` offers** for IDE0280, IDE0250 and IDE0251 (written only if you
  agree) keep the severities of the project's profile. An entry under a root `[*.cs]` section also applies to
  projects in the repository that do not use the package; with `EnforceCodeStyleInBuild` on, their builds then
  report these three.

No diagnostic ids were added or removed and no severity changed.

### Rules and analyzers

- CA1710's full-hierarchy check makes FDG3049, FDG3053, FDG3056, FDG3059, FDG3076 and FDG8005 fully checked
  (coverage `full`).
- The FDGA003 and FDGA023 code fixes: a comment in an empty `catch { /* ignore */ }` moves into the block; a block
  comment before a list item stays with that item; a switch section with two blocks is fixed in one pass.
- FDG5077 and FDG6019 report only at the end of a compilation and are now tagged so (`CompilationEnd`). IDEs may
  show them only with full solution analysis.
- Reference file names are all lowercase, for example `appa-coding-style.md`.

### What the agent sees

- The skill references now also list the rules that tools check only in part, marked "(partly checked)", so the
  agent and the reviewer see the part the tools miss. Large chapters are split into more reference files.
- A naming diagnostic on an existing type stays existing API when the turn adds a base type, so Stop never asks the
  agent to rename a type that may already be published. Other diagnostics on a type declaration are treated as new
  when the turn changed the type's base list (for example a faulty `Dispose` added with `IDisposable`).
- A miscased visible `const` quotes the rule for visible fields (FDG3077) as well as the one for constants.
- `/fdg-setup` offers to set the severities of IDE0280, IDE0250 and IDE0251 in the root `.editorconfig`, so that
  `dotnet fdg fix` and Stop apply their fixes instead of reporting them.
- `/fdg-setup` treats an answer to its questions or a requested change as a change to the plan, not as a
  confirmation: it shows the updated list of files again and waits until you confirm it.
- All Stops of one turn share one auto-fix record, and a file reformatted throughout is summarized instead of
  listed range by range.

### Hooks and CLI

- Stop is faster: a warm single-file Stop measured 12.22 s, against 17.21 s with 0.2.0
  ([performance](docs/performance.md)). The second, verification-only `dotnet format` pass runs only when a
  diagnostic it could fix is still present, and a project is built without its dependencies when nothing under
  them changed.
- The hook wrappers:
  - start one process per hook: the separate `dotnet-fdg --version` probe is gone;
  - show `FDG: dotnet-fdg failed:` with the first line of the tool's error when it exits non-zero without output,
    instead of failing silently;
  - turn a non-UTF-8 error line into valid UTF-8 and valid JSON;
  - run the `dotnet-fdg` pinned in the repository's local tool manifest when there is one;
  - also look for the tool under `DOTNET_CLI_HOME`, and the sh wrapper uses `USERPROFILE` under Git Bash on
    Windows, where `HOME` can differ.
- The Codex hook commands read the plugin root from the `PLUGIN_ROOT` environment variable, so a plugin path with
  `$`, a backtick or a quote works. **Codex users upgrading from 0.2.0 must approve the three hooks once more in
  `/hooks`.**
- Onboarding also counts a package reference that comes from an import the check does not follow, when the
  restore's `project.assets.json` lists the package.
- Edge cases fixed:
  - repository paths with `;` or `,` in the build properties and the SARIF log path;
  - a build marker that belongs to another project;
  - a stale `obj/fdg` marker after a switch to `UseArtifactsOutput` (the newer of the two markers wins);
  - a symbolic `LangVersion` such as `latest`, now shown as the version it stands for when that can be resolved;
  - a forced rebuild when every result is in a linked `Compile` item;
  - state files left half-written by a timeout (they are now written atomically);
  - a session start inspection that could not be cancelled and had no time limit;
  - a search for owning projects outside a git repository that walked up to the drive root;
  - `dotnet fdg fix` reporting "fixed" for files that do not compile;
  - include lists longer than the Windows command line, now split into several `dotnet format` runs.

  [docs/install.md](docs/install.md#known-limitations) lists what remains.
- Stop and `dotnet fdg fix` repair the mixed line endings `dotnet format` can leave in a file, giving it back its
  original line ending. A file converted throughout by a configured `end_of_line` is left as converted.

### Requirements and release engineering

- The minimum .NET SDK is 10.0.100 (was 8.0.100). An older SDK gets a session start warning, and the hooks still run.
  CI runs the integration tests on 10.0.100.
- GitHub Actions are pinned to commit SHAs on versions that run on Node.js 24.
- The generator resolves the SDK from the current directory, reports the file, line and column of a YAML error, and
  fails when two rules write different values to the same globalconfig key.
