# Phase 2 backlog

These are known issues and open decisions left over from the vertical slice (chapter 3 and appendix A). None of them blocks a preview release. Pilot findings and their measurements are in [pilot-2026-10.md](pilot-2026-10.md).

## Before the first release

The design calls for full coverage of the book before version 1.0. Until then, releases are previews.

- Write a README:
  - a statement of independence from the book's authors and publisher;
  - install steps for both hosts;
  - which chapters are covered;
  - the minimum SDK and VS versions;
  - the optional "run `dotnet fdg fix` first, commit it separately, and add the commit to `.git-blame-ignore-revs`" workflow.
- Set a preview version, for example `0.1.0-preview.1`.
- Publish the analyzer package and `dotnet-fdg` to nuget.org. Once they are published, check that `docs/install.md` works exactly as written.
- Add a CI job on SDK 8.0.1xx running the integration tests, or raise the documented minimum SDK to a tested version. `.slnx` needs SDK 9.0.200 or later.
- Pin GitHub Actions to commit SHAs.
- Not yet verified on a real host:
  - the Codex `$fdg-setup` invocation;
  - Codex SessionStart after compaction;
  - re-approving Codex hooks after a plugin update;
  - the interactive confirmation path of `/fdg-setup`.

## Before extending to more chapters

- The extractor flattens `ul`, `ol` and `table` inside sidebars, as well as inline emphasis, links and `<br>`. Later chapters use these.
- `generate` does not delete stale files in `references/`, and the drift check does not see extra files.
- Decide on ligature artifacts copied from the epub text, such as "Conf licts" in 3.4.1. The choice is between fidelity to the source and clean text.
- `verify-source`, `extract` and `check` print stack traces on malformed YAML, on a missing `dotnet`, and on schema load errors.
- `check` depends on the SDK-internal layout of `DotnetTools/dotnet-format`. It also resolves the SDK from the parent directory of `--rules`, not from the current directory.

## Rule mapping

- Map IDE1006 to FDG rules by symbol kind:
  - tag each naming rule in the YAML with its symbol kinds (type, member, parameter, private field, visible field, const, local, namespace);
  - keep `startColumn` from SARIF;
  - pick rules by the kind of failure, for example "must begin with upper case" or "Missing prefix".

  Today, a many-rule diagnostic quotes no rule text unless the message names one. The prefix messages can still pair FDGA055 with an unrelated second rule.
- FDGA023 reports only when the opening line exceeds the limit. Decide whether it should also report multi-line lists that are not chopped before the first item; the pilot's Codex session wrote one.
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
