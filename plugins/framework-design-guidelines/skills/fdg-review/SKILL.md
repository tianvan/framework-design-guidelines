---
name: fdg-review
description: >-
  Review C# code against the Framework Design Guidelines rules that tools cannot check (naming, API design, coding
  style), quoting each rule verbatim with its FDG id. Gives findings only and changes no code. Use when the user runs
  /fdg-review (or `$fdg-review` on Codex) or asks for an FDG or API design review.
disable-model-invocation: true
argument-hint: "[<path>...]"
---

# Review C# against the Framework Design Guidelines

Paths to review: $ARGUMENTS

If the line above is empty or still shows a placeholder (Codex does not substitute it), take the paths from the
user's message. With no paths, review the current changes: `git diff HEAD` (staged and unstaged) plus the untracked
`.cs` files (`git ls-files --others --exclude-standard -- "*.cs"`). In a repository with no commits yet
(`git rev-parse --verify HEAD` fails), review every tracked and untracked `.cs` file instead. Review only `.cs` files.

This is a review: give findings and change no files.

## Where to run it

- **Claude Code**: delegate the review to the `fdg-reviewer` subagent (named
  `framework-design-guidelines:fdg-reviewer` when the plugin namespaces it) and pass it: the paths, or that the scope
  is the current changes; the baseline, if the user named one; the path of the design specification, if the change
  comes with one (step 6); and the absolute path of the rule references, which is this skill's base directory
  followed by `../fdg-write/references`. Show the user its findings as it returns them.
- **Codex** (no subagents): review in this session by following the steps below.

## Steps

1. Collect the scope as above. For changes, review the changed and added lines and the declarations they belong
   to, not the rest of the file.
2. Find each file's profile: the `FrameworkDesignGuidelinesProfile` of the project that contains it (in the
   project file or a `Directory.Build.props` above it, or `profile` in the project's `obj/fdg/marker.json`).
3. Read the rule references for the topics the code in scope touches. Read
   [ch03-naming.md](../fdg-write/references/ch03-naming.md) for every review outside `test` projects and
   [appA-coding-style.md](../fdg-write/references/appA-coding-style.md) for every review; add the others by topic:

   | When the code in scope | Read |
   |---|---|
   | Shapes a public API's scenarios, entry points, layers or documentation | [ch02-fundamentals.md](../fdg-write/references/ch02-fundamentals.md) |
   | Declares a class, struct, interface or abstract class, or organizes types into namespaces | [ch04-types-4-1-to-4-4.md](../fdg-write/references/ch04-types-4-1-to-4-4.md) |
   | Declares a static class, interface, struct, enum, nested type or strongly typed string, or sets assembly metadata | [ch04-types-4-5-on.md](../fdg-write/references/ch04-types-4-5-on.md) |
   | Overloads members, implements interface members explicitly, or picks a property over a method | [ch05-members-general.md](../fdg-write/references/ch05-members-general.md) |
   | Declares properties, indexers, constructors or static constructors | [ch05-members-properties-constructors.md](../fdg-write/references/ch05-members-properties-constructors.md) |
   | Declares events, event handlers or fields | [ch05-members-events-fields.md](../fdg-write/references/ch05-members-events-fields.md) |
   | Declares extension methods, operators or conversion operators | [ch05-members-extensions-operators.md](../fdg-write/references/ch05-members-extensions-operators.md) |
   | Declares or validates parameters, or uses tuples in signatures | [ch05-members-parameters.md](../fdg-write/references/ch05-members-parameters.md) |
   | Declares virtual, protected or sealed members, callbacks, abstractions or base classes | [ch06-extensibility.md](../fdg-write/references/ch06-extensibility.md) |
   | Throws, catches or declares exceptions, or has Try methods | [ch07-exceptions.md](../fdg-write/references/ch07-exceptions.md) |
   | Uses arrays, attributes or collections in signatures | [ch08-usage-arrays-attributes-collections.md](../fdg-write/references/ch08-usage-arrays-attributes-collections.md) |
   | Uses `DateTime`, `DateTimeOffset`, `IComparable<T>`, `Nullable<T>`, `Uri` or XML types, overrides `Equals`, `GetHashCode` or `ToString`, or overloads `==` | [ch08-usage-types.md](../fdg-write/references/ch08-usage-types.md) |
   | Declares async methods, aggregate components or dependency properties | [ch09-patterns-aggregate-async.md](../fdg-write/references/ch09-patterns-aggregate-async.md) |
   | Implements `IDisposable`, `IAsyncDisposable` or a finalizer | [ch09-patterns-dispose.md](../fdg-write/references/ch09-patterns-dispose.md) |
   | Declares factories or LINQ support | [ch09-patterns-factories-linq.md](../fdg-write/references/ch09-patterns-factories-linq.md) |
   | Has optional features or variant generic parameters | [ch09-patterns-optional-variance.md](../fdg-write/references/ch09-patterns-optional-variance.md) |
   | Has template methods, timeouts or XAML-facing types | [ch09-patterns-template-timeouts-xaml.md](../fdg-write/references/ch09-patterns-template-timeouts-xaml.md) |
   | Takes or returns spans, memory or other buffers | [ch09-patterns-buffers.md](../fdg-write/references/ch09-patterns-buffers.md) |
   | Has Try-Write or `OperationStatus` methods | [ch09-patterns-buffers-try-write.md](../fdg-write/references/ch09-patterns-buffers-try-write.md) |
   | Changes a library's public or protected surface (step 5) | [appd-breaking-changes-d-1-to-d-8.md](../fdg-write/references/appd-breaking-changes-d-1-to-d-8.md), [appd-breaking-changes-d-9-on.md](../fdg-write/references/appd-breaking-changes-d-9-on.md) |

   In `test` projects read only the Chapter 7, Chapter 8 and Appendix A references; the other chapters are off
   there, and skip the rules marked "(library only)" or "(not in test projects)" in the references you read. In
   `application` projects skip the rules marked "(library only)": they are about a library's public surface and are
   off there. The references list the rules tools do not enforce; follow a rule's `examples:` link
   when its wording is not enough.
4. Check every declaration in scope against those rules. Report a finding only when a specific rule clearly applies
   to a specific line. Do not invent advice the rules do not support, and do not report what analyzers and
   `dotnet format` already fix or report (layout, modifiers, and the casing of an identifier's first letter). A rule
   tagged `[C# N+]` applies only when the project's language version is at least N; below it, its `(alt: ...)` is the
   expectation.
5. For files in `library` projects, compare the public and protected surface with a baseline: the one the user
   names, or else `git merge-base HEAD <default branch>`, where the default branch is the output of
   `git symbolic-ref --short refs/remotes/origin/HEAD` (for example `origin/main`), or `main` when that fails. If
   the merge-base cannot be computed either, say so, ask the user for a baseline, and skip this step. Run
   `git diff <baseline> -- <file>` and check each changed public or protected declaration against the Appendix D
   entries. A NOT PERMITTED match is a finding. For a CONTEXTUAL match, read the book's reasoning for that entry
   behind its `examples:` link, and report it only when the conditions it gives hold for this change; say which. A
   PERMITTED match needs no finding. Private and internal members are out of scope for this step, except private
   fields of serializable types (FDGD034).
6. When the review covers a library's public API and the change comes with a design specification, check that the
   specification has the parts of the book's sample specification (Appendix C), citing the rule that asks for each:
   - an introduction with an overview of the feature, ahead of the scenarios (FDG2005);
   - the main scenarios, each with code samples written before the object model (FDG2005, FDG2007);
   - goals and non-goals, which set the scope of the feature (FDG2004);
   - the API itself and its behavior, with the naming choices discussed (FDG2004, FDG2022);
   - questions raised in review, with their answers (FDG2004).

## Output

One finding per rule and line, ordered by file and line, in exactly this form:

```
<file>:<line> — FDG#### <STRENGTH> <rule text>
  <one sentence: why the line breaks the rule and what would follow it>
```

The strength and rule text are copied verbatim from the reference, without `(library only)`, `(not in test projects)`,
`(context: ...)` or `(can cause: ...)`, the version tag, `(alt: ...)` or the `examples:` link. For example:

```
src/Orders/OrderMgr.cs:12 — FDG3014 DO NOT use abbreviations or contractions as part of identifier names.
  `OrderMgr` abbreviates "Manager"; `OrderManager` follows the rule.
```

For an Appendix D finding, the verdict and a colon take the place of the strength, and the explanation names the
baseline:

```
src/Orders/OrderService.cs:20 — FDGD037 NOT PERMITTED: Renaming a Method Parameter
  `id` was `orderId` at the baseline; callers that pass it by name no longer compile, so keep `orderId`.
```

With no findings, say that the code in scope follows the FDG judgment rules. End by asking the user which findings,
if any, to apply, and change nothing until they answer.
