---
name: fdg-review
description: >-
  Review C# code against the Framework Design Guidelines rules that tools cannot check (naming, API design, coding
  style), quoting each rule verbatim with its FDG id. Gives findings only and changes no code. Use when the user runs
  /fdg-review or asks for an FDG or API design review.
disable-model-invocation: true
argument-hint: "[<path>...]"
---

# Review C# against the Framework Design Guidelines

Paths to review: $ARGUMENTS

If the line above is empty or still shows a placeholder (Codex does not substitute it), take the paths from the
user's message. With no paths, review the current changes: `git diff HEAD` (staged and unstaged) plus the untracked
`.cs` files (`git ls-files --others --exclude-standard -- "*.cs"`). Review only `.cs` files.

This is a review: give findings and change no files.

## Where to run it

- **Claude Code**: delegate the review to the `fdg-reviewer` subagent (named
  `framework-design-guidelines:fdg-reviewer` when the plugin namespaces it) and pass it: the paths, or that the scope
  is the current changes; and the absolute path of the rule references, which is this skill's base directory
  followed by `../fdg-write/references`. Show the user its findings as it returns them.
- **Codex** (no subagents): review in this session by following the steps below.

## Steps

1. Collect the scope as above. For changes, review the changed and added lines and the declarations they belong
   to, not the rest of the file.
2. Find each file's profile: the `FrameworkDesignGuidelinesProfile` of the project that contains it (in the
   project file or a `Directory.Build.props` above it, or `profile` in the project's `obj/fdg/marker.json`). In
   `test` projects the Chapter 3 rules are off.
3. Read the rule references: [ch03-naming.md](../fdg-write/references/ch03-naming.md) for names and
   [appA-coding-style.md](../fdg-write/references/appA-coding-style.md) for coding style. They list
   the rules tools do not enforce; follow a rule's `examples:` link when its wording is not enough.
4. Check every declaration in scope against those rules. Report a finding only when a specific rule clearly applies
   to a specific line. Do not invent advice the rules do not support, and do not report what analyzers and
   `dotnet format` already fix or report (layout, casing, modifiers). A rule tagged `[C# N+]` applies only when the
   project's language version is at least N; below it, its `(alt: ...)` is the expectation.

## Output

One finding per rule and line, ordered by file and line, in exactly this form:

```
<file>:<line> — FDG#### <STRENGTH> <rule text>
  <one sentence: why the line breaks the rule and what would follow it>
```

The strength and rule text are copied verbatim from the reference, without the version tag, `(alt: ...)` or
`examples:` link. For example:

```
src/Orders/OrderMgr.cs:12 — FDG3014 DO NOT use abbreviations or contractions as part of identifier names.
  `OrderMgr` abbreviates "Manager"; `OrderManager` follows the rule.
```

With no findings, say that the code in scope follows the FDG judgment rules. End by asking the user which findings,
if any, to apply, and change nothing until they answer.
