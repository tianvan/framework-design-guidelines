# Phase 2 backlog

These are known issues and open decisions left over from the vertical slice (chapter 3 and appendix A) and from phase 2 (the rest of the book). They go to phase 3. None of them blocks a preview release. Pilot findings and their measurements are in [pilot-2026-10.md](pilot-2026-10.md).

## Before the first release

The design calls for full coverage of the book before version 1.0. Until then, releases are previews.

- Add a CI job on SDK 8.0.1xx running the integration tests, or raise the documented minimum SDK to a tested version. `.slnx` needs SDK 9.0.200 or later.
- Pin GitHub Actions to commit SHAs, on versions that run on Node.js 24 (CI warns that Node.js 20 is deprecated).
- Not yet verified on a real host:
  - the Codex `$fdg-setup` invocation;
  - Codex SessionStart after compaction;
  - re-approving Codex hooks after a plugin update;
  - the interactive confirmation path of `/fdg-setup`.
- `ExistingApi` judges a diagnostic only by its location: when `IDisposable` and a faulty `Dispose` are added to an
  existing type, CA1063 is reported on the type's name and counted as existing API, so Stop does not send it back. For
  a diagnostic on a type declaration, also compare the base type list with the baseline.
- The reference file names mix casing: `appA-coding-style.md` keeps the appendix letter's case, while the split names
  such as `appd-breaking-changes-d-1-to-d-8.md` must be lowercase. Pick one before the paths are relied on.

## Generator

- `check` depends on the SDK-internal layout of `DotnetTools/dotnet-format`. It also resolves the SDK from the parent directory of `--rules`, not from the current directory.
- A one-line YAML error names neither the file nor the position, although `YamlException.Start` has it.
- The generator emits no `customTags`. The analyzers that report only at compilation end (FDG5077 in `DuplicateExtensionMethodAnalyzer`, FDG6019 in `BaseSuffixAnalyzer`) lack `WellKnownDiagnosticTags.CompilationEnd` (RS1037).

## Rule mapping

- CA1710 keeps its default `exclude_indirect_base_types = true`, so a type that derives from a collection or exception type through another base is not reported; FDG3049, FDG3053, FDG3056, FDG3059 and FDG3076 are coverage partial for that reason. The book's text covers the whole hierarchy. Setting the option to `false` needs a noise run over FDG3048 to FDG3059, FDG3076, FDG7057, FDG8005, FDG8038 and FDG8039 first (Ruling 48).
- `DeclarationLocator.FieldKind` classifies every `const` as `constField` before it looks at visibility, so a miscased public const quotes only FDGA053, never FDG3077 or FDG3001, although FDG3077's own example is `public const MinValue`. Adding `constField` to FDG3077's tags would quote it for private consts too; classify a visible const as both kinds instead (Ruling 54).
- `dotnet format` applies the fix of an IDE rule that has no option (IDE0280, IDE0250, IDE0251) only when its severity is in an `.editorconfig`; the globalconfig's `dotnet_diagnostic` entry is not enough (probe with SDK 10.0.402), so `dotnet fdg fix` and the Stop hook do not apply those fixes unless the project's `.editorconfig` sets their severity.
- Partial `sdk` and `fdg` DO rules stay out of the skill references (spec §6), so the AI does not see the part of the rule the check misses. Decide whether listing them is worth the larger reference files (Ruling 22).
- When two rules write the same globalconfig key, the generator takes the IDE severity from the first rule only (`GlobalConfigEmitter`). The current data has no such case.

## Hooks and CLI

- **Stop time:** 21 s warm and 27–31 s cold for a single-file change.
  - Run the second, verification-only `dotnet format` pass only when SARIF still shows FDGA003 or FDGA023 in the changed files (about 8 s).
  - Build with `--no-dependencies` when no referenced project changed.
  - Measure Stop on multi-project turns; builds are serial.
  - Consider a resident process only if the common case is clearly over 30 s.
- **Turn summaries:**
  - Merge the auto-fix records of all Stops in a turn; today a later Stop overwrites them.
  - Say "reformatted throughout" instead of listing dozens of ranges.
- **Wrappers:**
  - Avoid the separate `dotnet-fdg --version` probe on every hook.
  - The fallback location ignores `DOTNET_CLI_HOME`, and the sh wrapper uses `$HOME` on Windows.
  - The sh wrapper passes non-UTF-8 stderr through unchanged.
  - A version pinned in a local tool manifest does not apply to the hooks.
- **Codex on Windows:** the `${PLUGIN_ROOT}` path sits inside a double-quoted PowerShell string, so `$` or a backtick in the user directory would be expanded. Changing `hooks.codex.json` makes every user re-approve the hooks, so do it together with another hook change. Add a test with a plugin path that contains spaces and CJK characters.
- **Onboarding:** `obj/project.assets.json` only vetoes. A project that references the package through an import the check does not follow is onboarded in MSBuild but skipped by the hooks.
- **Edge cases:**
  - Paths containing `;` or `,` break `-p:FdgSarifDir`, `-p:FdgSarifProject` and `ErrorLog`.
  - `ProjectInfoProvider` reads a marker without checking that it belongs to the project, unlike `SessionContext`.
  - Stale `obj/fdg` markers win over `artifacts/` after switching to `UseArtifactsOutput`.
  - A symbolic LangVersion is shown as "C# latest".
  - `IsForeign` forces a rebuild when every result is in a linked `Compile` item.
  - The `formatted/` copies and `ai-lines.json` are not written atomically when a timeout hits.
  - Inspect in SessionStart is synchronous and cannot be cancelled.
  - In a non-git directory, `FindOwningProjects` walks up to the drive root.
  - `fix` prints "fixed" for files that do not compile.
  - A per-file `--include` list can exceed the Windows command-line limit in large directories.
- **Known limitations, documented in `docs/install.md`:**
  - concurrent sessions or IDE edits during a turn;
  - extracting a class while the source file is kept, which reports the moved old code;
  - submodules;
  - custom output paths;
  - commits rebased or cherry-picked during a turn.
- **Docs:** `docs/install.md` should say that the hooks run `dotnet build` and `dotnet format` in the repository, so use them only in trusted repositories.

## Repository hygiene

- The repo's own `.editorconfig` private field rule has no exception for `const`.
- FDGA003 and FDGA023 code fixes:
  - `catch { /* ignore */ }` keeps the comment on the brace line;
  - a `/* c */` before a list item moves with the comma;
  - a switch section with two blocks needs two passes;
  - a tab counts as one column in line length.
