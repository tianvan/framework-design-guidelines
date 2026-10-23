---
name: fdg-fix
description: >-
  Make the given C# code pass every Framework Design Guidelines tool check: run dotnet fdg fix, build, then fix the
  remaining FDG, CA and IDE diagnostics and compiler errors project by project. Use when the user runs /fdg-fix (or
  `$fdg-fix` on Codex) or asks to clean up a directory, project or file with FDG.
disable-model-invocation: true
argument-hint: "[<path>...]"
---

# Fix FDG issues

Paths to fix: $ARGUMENTS

If the line above is empty or still shows a placeholder (Codex does not substitute it), take the paths from the
user's message. With no paths at all, use the current directory. A path can be a directory, a `.csproj`, a `.sln` or
`.slnx`, or a `.cs` file.

Ground rules:

- Change only files in the working tree. Never run `git add`, `git commit`, `git stash` or `git checkout`, and never
  create a branch. The user reviews the result with `git diff`.
- Never apply a breaking change to a public API without the user's confirmation (step 4).
- Do not review the code against the judgment rules here; that is `/fdg-review`, run on a small scope when the user
  wants it.

## Step 1: Run the CLI

Note `git diff --stat` first, so you can tell the CLI's changes from the user's. Then run:

```
dotnet fdg fix <paths>
```

It fixes layout, applies the non-breaking fixes of the diagnostics FDG registers (no other analyzer's), and prints
no report. If `dotnet fdg` is not installed, tell the user to run `dotnet tool install -g dotnet-fdg` and stop. It skips projects
that are legacy (non-SDK) or have no `FrameworkDesignGuidelinesProfile`; name any such project the paths cover and
suggest `/fdg-setup`.

## Step 2: Collect the remaining issues

The projects to check are the onboarded SDK-style projects the paths cover (the project that contains a `.cs` file).
For each one:

1. Read `obj/fdg/marker.json` in the project directory. If it is missing, the project has not been built since it
   was onboarded: build it once (`dotnet build <the .csproj>`) and read it again; if it is still missing, the project
   is not onboarded, so report it and suggest `/fdg-setup`. The marker's `projectPath` is the project's full path as
   MSBuild sees it, and its `rulesPath` is the package's `rules.json`. From that file take `registeredDiagnostics`
   (the diagnostic ids FDG reports), `breakingFixDiagnostics` (ids whose fix renames or moves a symbol), and `rules`
   (each rule's `id`, `strength`, `text` and the `diagnostics` that check it).
2. Create a new, empty directory under the system temporary directory (`$TMPDIR`, `$TEMP` or `/tmp`), never inside
   the repository, and build with the marker's `projectPath`, quoted, as both the project and `FdgSarifProject`:

   ```
   dotnet build <projectPath> --no-restore -p:FdgSarifDir=<temp dir> -p:FdgSarifProject=<projectPath>
   ```

   Only the project named by `FdgSarifProject` writes its log, as `<temp dir>/<target framework>.sarif` (one per
   target framework); projects it references do not. If the build fails because the project is not restored, run
   `dotnet restore` on it and build again. If the build succeeds but writes no `.sarif` file, the compiler was
   skipped as up to date; build again with `--no-incremental` added. If there is still no `.sarif` file, tell the
   user "<project>: diagnostics could not be checked (no SARIF log)", list it again in the summary, and never treat
   the project as clean. Delete the temporary directory once you have read the logs.
3. Read every `.sarif` file. Keep a result when it is not suppressed (no `suppressions`) and either its `ruleId` is
   in `registeredDiagnostics` with `level` `warning` or `error`, or it is a compiler error (`ruleId` starting with
   `CS`, `level` `error`). Ignore everything else, such as other analyzers' warnings and nullable warnings. The
   location is `locations[0].physicalLocation`: `artifactLocation.uri` (a percent-encoded file URI) and
   `region.startLine`.
4. For each kept result, find the rule it checks: the rule whose `id` is the `ruleId` (FDG analyzers), or whose
   `diagnostics` contain it (CA and IDE rules). Quote that rule's `strength` and `text` when you explain the fix.

## Step 3: Fix project by project

Fix one project at a time, then rebuild it as in step 2, into a new temporary directory, and check
that its issues are gone before you move on. This keeps the context small. Fix compiler errors first.

## Step 4: Renames and public API changes

- A symbol that is not visible outside its assembly (private, internal, or a member of a non-public type): rename it
  directly, with every reference in the solution.
- A breaking change to the public API (renaming, changing a signature or changing a type of a public or protected
  member of a public type), including every fix of a diagnostic in `breakingFixDiagnostics` on such a symbol: do not
  make it yet. List these changes (symbol, current form, proposed form, rule) and wait for the user to confirm
  which to apply. Apply only those; leave the rest as they are.

## Step 5: Verify and summarize

Run `dotnet fdg fix <paths>` and the builds from step 2 once more. Then report briefly:

- what the CLI fixed automatically (the files it changed, from `git diff --stat`);
- what you changed by hand, by project;
- what is left for the user to decide: unconfirmed public API changes and issues you could not fix, each as
  `<file>:<line> — <diagnostic id> <rule strength and text>`;
- the projects whose diagnostics could not be checked (no SARIF log), if any.

Remind the user that all changes are in the working tree for them to review with `git diff`.
