# Noise samples

How often the diagnostics enabled by the FrameworkDesignGuidelines.Analyzers package fire on real code, and how many of
those reports are false positives. `eng/noise/Measure-Noise.ps1` builds each sample with the package injected through
`CustomBeforeMicrosoftCommonProps` and `CustomAfterMicrosoftCommonTargets`, so no sample file is edited, and counts the
SARIF results by diagnostic.

## Samples

| Sample | Source | Commit | Project | Framework | Profile |
| --- | --- | --- | --- | --- | --- |
| serilog | https://github.com/serilog/serilog | `81af75f3a5e28ec9f5be18ef74357a9914a51745` | `src/Serilog/Serilog.csproj` | netstandard2.0 | library |
| polly | https://github.com/App-vNext/Polly | `3f3adad9d5af85658fc3585018f02a2d8c2173e8` | `src/Polly.Core/Polly.Core.csproj` | netstandard2.0 | library |
| newtonsoft-json | https://github.com/JamesNK/Newtonsoft.Json | `52fa3aef1f2cadcd3a3f874251eddc98d3efbbaa` | `Src/Newtonsoft.Json/Newtonsoft.Json.csproj` | netstandard2.0 | library |
| internal-a, internal-b | an internal application, anonymized | local clone | two projects | net472 | application |

The open-source samples are listed in `eng/noise/samples.json`. The internal application is listed in a local sample
file that is not committed; this page gives only counts and anonymized conclusions for it.

## Method

- **Counting.** Suppressed results are dropped, and results are deduplicated by (file, line, diagnostic). Only the
  sample's own project is counted, not the projects it references.
- **False positive.** A report is a false positive when the reported location actually complies with the book rule's
  text, including the exceptions the text states. Code that breaks the rule is a true warning, even if the code is
  otherwise reasonable. Reports are judged against the rule text and body in `rules/ch03-naming.yaml` and
  `rules/appA-coding-style.yaml`.
- **Threshold.** A diagnostic is over the threshold when more than 1 in 20 of its reports is a false positive. Over the
  threshold, chapter procedure R6 (this project's analyzers) or R7 (SDK diagnostics) applies.
- **Spot checks.** Up to 20 locations per diagnostic, evenly spaced over all samples' locations in order, so each sample
  is checked in proportion to its count. A diagnostic with 20 or fewer reports is checked in full.
- **Line endings.** IDE0055 also reports line endings that differ from `end_of_line`, or from the platform's newline
  when no `end_of_line` is set. Each remote sample is checked out with the line endings its own root `.editorconfig`
  sets for C# files (serilog, polly: LF), or the platform's (newtonsoft-json: CRLF on Windows), so that git settings do
  not show up as IDE0055 noise. With the opposite checkout, IDE0055 was 50 instead of 32 for serilog and 803 instead of
  676 for newtonsoft-json; no other diagnostic changed.
- **Sample configuration.** Each sample's own `.editorconfig` and globalconfig files still apply. polly ships its own
  `.editorconfig` (it turns off IDE0007 and IDE0008 and sets its own static field naming) and its own globalconfig files,
  which take precedence over the package's; one CA1708 key conflicts with the package
  (`MultipleGlobalAnalyzerKeys`). polly's low counts therefore measure polly's configuration as much as the package.
  The override listing also names each sample's `*.globalconfig` files and `dotnet_analyzer_diagnostic.*` entries.
  serilog has an `.editorconfig` with no diagnostic or naming entries; newtonsoft-json has none; the internal
  application overrides nothing that applies.
- **Restore.** Package restore came from nuget.org.

## Baseline: chapter 3 and appendix A

Package 0.1.0 (chapter 3 and appendix A). FP is false positives and True is true warnings among the spot-checked
reports. The FDGA023 and CA1708 rows are re-measured after the treatments in their row; the notes give the first
counts. FDGA004 replaces IDE0011 (see the notes).

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1507 | FDGA048 | 0 | 0 | 0 | 0 | 344 | 20 | 0 | 20 | None needed. |
| CA1707 | FDG3010, FDG3079 | 0 | 0 | 0 | 0 | 726 | 20 | 0 | 20 | None needed. |
| CA1708 | FDG3007 | 0 | 0 | 0 | 0 | 5 | 5 | 0 | 5 | Narrowed to the public API (`dotnet_code_quality.CA1708.api_surface = public`), because chapter 3 governs public names. See the notes. |
| CA1711 | FDG3023 and others | 0 | 0 | 0 | 1 | 3 | 4 | 0 | 4 | None needed. |
| CA1716 | FDG3012 | 12 | 0 | 0 | 0 | 0 | 12 | 0 | 12 | None needed. |
| CA1720 | FDG3016, FDG3018 (and FDG3017 until phase 2) | 0 | 0 | 0 | 0 | 1 | 1 | 1 | 0 | Over the threshold (n = 1). Already `suggestion` in every profile; no change. |
| CA1724 | FDG3032 | 0 | 0 | 0 | 0 | 3 | 3 | 2 | 1 | Over the threshold (n = 3). FDG3032 lowered to suggestion in phase 2; see the notes. |
| FDGA003 | FDGA003 | 0 | 0 | 0 | 1 | 9 | 10 | 0 | 10 | None needed. |
| FDGA004 | FDGA004 | 109 | 0 | 0 | 6 | 94 | 20 | 0 | 20 | R6: this project's analyzer, which exempts argument validation preambles (FDGA005), replaces IDE0011. |
| FDGA023 | FDGA023 | 141 | 89 | 986 | 20 | 84 | 20 | 0 | 20 | R6: narrowed so that an enclosing operator expression no longer makes a list long. Under the threshold after the narrowing. |
| IDE0008 | FDGA042 | 242 | 0 | 12 | 7 | 62 | 20 | 0 | 20 | None needed. |
| IDE0017 | FDGA043 | 0 | 0 | 37 | 0 | 33 | 20 | 0 | 20 | None needed. |
| IDE0028 | FDGA044 | 0 | 0 | 11 | 0 | 143 | 20 | 0 | 20 | None needed. |
| IDE0032 | FDGA046 | 2 | 0 | 38 | 0 | 36 | 20 | 0 | 20 | None needed. |
| IDE0036 | FDGA028 to FDGA037 | 0 | 0 | 24 | 0 | 0 | 20 | 0 | 20 | None needed. |
| IDE0040 | FDGA027 | 293 | 0 | 1 | 0 | 141 | 20 | 0 | 20 | None needed. |
| IDE0044 | FDGA049 | 4 | 0 | 4 | 0 | 1 | 9 | 0 | 9 | None needed. |
| IDE0055 | FDGA001 to FDGA021 | 32 | 0 | 676 | 16 | 46 | 20 | 1 | 19 | At the threshold (1 in 20), not over; no change. |
| IDE0130 | FDGA067 | 4 | 0 | 8 | 5 | 174 | 20 | 0 | 20 | None needed. |
| IDE1006 | FDG3001 and others | 33 | 0 | 157 | 0 | 1504 | 20 | 0 | 20 | None needed. |
| IDE2003 | FDGA025 | 19 | 0 | 408 | 1 | 165 | 20 | 0 | 20 | None needed. |

### Notes

- **FDGA023.** In the first measurement (serilog 142, polly 90, newtonsoft-json 1106, internal-a 20, internal-b 86),
  2 of 20 spot-checked reports were false positives: a line that is long only because an operator joins short calls (a
  conditional expression, or a condition joined by `||`). Each argument list was short, but the line crossed the guide;
  that is not an "exceedingly long" method invocation. Over all 1444 reports, about 78 (about 5.5%) were of this kind,
  most in newtonsoft-json (`condition ? A(x, token) : B(x, token)`). R6 treatment: a list that is not wrapped is now
  long only when its line stays longer than `max_line_length` after the outermost enclosing conditional, `||`, `&&` or
  `??` expression on the line is reduced to the list's own invocation, including the fluent chain it is part of. The
  re-measurement removed 121 reports, all on such lines (the estimate of 78 missed some `&&` and `==` conditions), and
  added none. A fresh spot check of 20 found no false positives. A later fix also exempts a list nested in the
  arguments of such a call (`flag ? Outer(Inner(a, b), c) : Other(a)`), which removed 3 more newtonsoft-json reports,
  counted by (file, line, column), and added none. Lines where the operator is inside the list itself (43
  reports, for example `F(a ?? b, c, d)`) are still reported, because the list is what makes the line long. About 12
  newtonsoft-json reports are lists that are chopped one item per line but whose closing parenthesis is on its own
  line. The rule text does not say where the parenthesis goes, but the rule's example keeps it on the last item's
  line, so these are counted as true warnings; they are debatable.
- **FDGA004 (was IDE0011).** IDE0011 first counted serilog 123, polly 0, newtonsoft-json 0, internal-a 28 and
  internal-b 98, and 5 of 20 spot-checked reports were false positives: all an argument validation preamble
  (`if (x == null) throw ...;`), which FDGA005 allows without braces. Lowering IDE0011 to `suggestion` (R7) would have
  dropped its fix from `dotnet fdg fix` for the 95% of reports that are true warnings, so R6 applies instead: the
  FDGA004 analyzer reports what IDE0011 reports and exempts the `if`...`throw` statements without `else` at the start
  of a member, accessor, local function or lambda body, up to the first blank line or other statement. Like IDE0011,
  it does not report a statement with a preprocessor directive inside it (one serilog `if` whose embedded statement is
  split by `#if`). FDGA004 counts serilog 109, internal-a 6 and internal-b 94, and the 20 spot-checked reports (taken
  before the directive exception, which removed one report that was not among them) are all true warnings: their embedded
  statement is a `return`, `continue`, assignment or call, or a `throw` inside a `catch` or after other code. One
  borderline case is an `if`...`throw` that follows a guard call (`Guard.AgainstNull(name);`); the book says code
  other than `if`...`throw` ends the preamble, so it is a true warning.
- **CA1724.** Two of the three reports name a type the same as a framework namespace that the type is not in; the
  FDG3032 text covers only "a type in that namespace". The sample was too small for a decision at first. In phase 2
  a probe confirmed that CA1724 compares a type name with the namespaces of referenced assemblies too, so these
  reports are not violations: FDG3032 is now a suggestion in the library and application profiles (2 false positives
  of 3, as for FDG4022 and FDG4044), and its notes say so.
- **CA1708.** In the first measurement (internal-b 70), all 20 checked reports were a private field and a public
  property whose names differ only by case. The rule text states no exception, so they were true warnings, but chapter
  3 governs public names, and polly's own configuration also limits CA1708 to the public API surface. FDG3007 now sets
  `dotnet_code_quality.CA1708.api_surface = public`. The 5 remaining reports are all public members that differ only
  by case.
- **IDE1006.** For `[ThreadStatic]` fields the message names the `s_` prefix instead of `t_`, but each checked field
  lacked any prefix, so the reports are true warnings.
- **IDE0055.** The false positive is attribute spacing on a parameter (`[NotNullWhen(true)]out`), which no book rule
  covers. Most true warnings are `case` block braces that are not indented (FDGA020), brace spacing (FDGA008) and
  trailing whitespace.
- **Unmapped diagnostics.** The package sets `EnableNETAnalyzers`, which also brings the SDK's default `note`
  diagnostics that no book rule maps (CA1041, CA1050, CA1070, CA1514, CA1816, CA1822, CA1834, CA1854, CA1858, CA1859,
  CA1860, CA1861, CA2016, CA2211, CA2263; 1 to 28 each). The samples' own compiler warnings (CS0108, CS0612, CS0649) and
  polly's own analyzers (S1694, S2094) also appear in the logs. None of these is counted above.

## Chapter 3 analyzers

Package 0.1.0 with the chapter 3 naming analyzers (Task 12), enabled by the profile globalconfigs (library for the
open-source samples, application for the internal ones). Only the new diagnostics are counted. Every report was
checked (20 in all), because no diagnostic had more than 20.

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| FDG3003 | FDG3003 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG3004 | FDG3004 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | None needed. |
| FDG3013 | FDG3013 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | None needed. |
| FDG3033 | FDG3033 | 1 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | None needed. |
| FDG3051 | FDG3051 | 0 | 0 | 3 | 0 | 3 | 6 | 0 | 6 | None needed. |
| FDG3068 | FDG3068 | 0 | 0 | 0 | 0 | 11 | 11 | 0 | 11 | None needed. |
| FDG3075 | FDG3075 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG3087 | FDG3087 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |

### Notes

- **FDG3004.** An enum member named in capitals after the acronym (`XML`).
- **FDG3013.** An enum member named in Chinese characters; the diagnostic reports only the first, because the members
  share a line and counts are deduplicated by line.
- **FDG3033.** A public static class named `Log`, exactly one of the names the rule text lists. Added after the
  cross-check (measured in a second run, with the other counts unchanged).
- **FDG3051.** In newtonsoft-json, two delegates used as property types and one used as a constructor factory, none by
  an event. In internal-b, three generic overloads of one "value changed" delegate that no event in the project uses.
  The rule text names only event handlers as the exception, so all are true warnings.
- **FDG3068.** Public properties of `List<T>`, `ObservableCollection<T>` or array type named with a `List` suffix,
  several also camel-cased (which IDE1006 reports separately).
- **No reports.** FDG3003, FDG3075 and FDG3087 report nothing on these samples; their tests cover the positive cases.
  The open-source samples follow the book's casing, and the internal projects declare no public resource classes and
  no custom event handler delegates. FDG3087 is off in the application profile (library only), so it did not run on
  the internal samples.
- **Rejected at gate 1 (not measured).** FDG3069 (negative Boolean property names): the rule text gives one example
  and no word list, so a check would have to guess intent. Its notes say so.

### Phase 2 additions: FDG3006 and FDG3086

Package 0.1.0 with the two analyzers added in the phase-2 re-check (Ruling 50), measured with
`-Diagnostics FDG3006,FDG3086`, and again after the Task 27 fix round, which made FDG3006 report plurals and
never join words across a digit or underscore. Every report was checked (19 in all).

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| FDG3006 | FDG3006 | 0 | 0 | 2 | 6 | 11 | 19 | 0 | 19 | None needed. |
| FDG3086 | FDG3086 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |

- **FDG3006.** Every report is a form from the Not column of Table 3-2: `Whitespace` in two newtonsoft-json writer
  methods; `FileName`, `fileName` or the plural `fileNames` (13), `UserName` or `userName` (3) and `UserID` (1) in the
  internal samples. The fix round added only the `fileNames` report.
- **FDG3086.** No sample declares an operator with an indexed parameter name; the tests cover the positive cases.

## Chapter 2 mappings

Chapter 2 (Framework Design Fundamentals, FDG2001 to FDG2032) maps no SDK diagnostic and adds no analyzer: all 32
rules are judgment rules about the design process and the consumer experience of an API. No new diagnostics, so
nothing was measured.

## Chapter 4 mappings and analyzers

Package 0.1.0 with the chapter 4 mappings and the new type design analyzers, enabled by the profile globalconfigs
(library for the open-source samples, application for the internal ones). Only the newly mapped SDK diagnostics and
the new analyzers are counted. Every report was checked, except FDG4032 (37; 20 checked) and CA1050 (28; 20 checked).

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1008 | FDG4040, FDG4048, FDG4049 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | None needed. |
| CA1012 | FDG4013, FDG4014 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | None needed. |
| CA1014 | FDG4057 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA1016 | FDG4058 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA1017 | FDG4060 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | Coverage narrowed to partial (see notes). |
| CA1027 | FDG4044 | 0 | 0 | 0 | 0 | 2 | 2 | 1 | 1 | FDG4044 lowered to suggestion. |
| CA1028 | FDG4041 | 0 | 0 | 0 | 0 | 6 | 6 | 0 | 6 | None needed (already a suggestion). |
| CA1034 | FDG4053 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA1040 | FDG4022 | 0 | 0 | 0 | 0 | 1 | 1 | 1 | 0 | FDG4022 lowered to suggestion. |
| CA1050 | FDG4006 | 0 | 0 | 0 | 0 | 28 | 20 | 0 | 20 | None needed. |
| CA1052 | FDG4019 | 0 | 0 | 0 | 0 | 6 | 6 | 0 | 6 | None needed. |
| CA1069 | FDG4039 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA1700 | FDG4037 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA2217 | FDG4044, FDG4045 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| IDE0250 | FDG4028 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| IDE0251 | FDG4029 | 3 | 0 | 9 | 0 | 0 | 12 | 0 | 12 | None needed. |
| FDG4013 | FDG4013 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG4026 | FDG4026 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG4027 | FDG4027 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG4032 | FDG4032 | 1 | 35 | 0 | 0 | 1 | 20 | 0 | 20 | None needed. |
| FDG4038 | FDG4038 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG4056 | FDG4056 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG4059 | FDG4059 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG4061 | FDG4061 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG4062 | FDG4062 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |

### Notes

- **polly's configuration.** polly's own globalconfig files set CA1008, CA1014, CA1017, CA1028, CA1034, CA1040,
  CA1050 and CA1700 to `none` and take precedence over the package, so polly's zeros for those diagnostics measure
  polly's configuration, not the package.
- **Library-only rules on the internal samples.** CA1014, CA1016, CA1017, CA1034, FDG4038, FDG4056, FDG4059, FDG4061
  and FDG4062 are off in the application profile, so they did not run on internal-a and internal-b.
- **CA1040.** The one report is an empty interface that eight classes implement and that is used as the type
  of a property and a local variable: a marker checked at compile time, which the FDG4022 body allows. 1 false positive
  of 1 is above 1 in 20, so FDG4022 is now a suggestion in both profiles.
- **CA1027.** One report is a `[Flags]`-style enum of 1, 2, 4 and 8 tested with `&` (true). The other is a simple enum
  with the values 0 and 2, which CA1027 takes for flags (false positive). 1 of 2 is above 1 in 20, so FDG4044 is now a
  suggestion; CA2217 keeps warning severity through FDG4045.
- **CA1017.** No sample was reported, although serilog has public types and no `ComVisibleAttribute`. A test project
  showed that with SDK 10.0.402, CA1017 reports only an assembly with a public type in the global namespace. FDG4060's
  coverage is now partial, and its notes say so.
- **CA1014, CA1016, FDG4059, FDG4061 and FDG4062 on the open-source samples.** serilog and newtonsoft-json declare
  `CLSCompliant(true)`, and their projects set `Description` and `Copyright`, so the SDK generates every attribute
  FDG4059 and FDG4061 ask for. The generated file version has four parts. A test project without these properties
  reported CA1014, FDG4059 (AssemblyDescription) and FDG4061 (AssemblyCopyright) as expected. Diagnostics without a
  source location are counted with an empty path.
- **CA1050.** Public types declared outside any namespace in internal-b (models and parameter holders).
- **CA1052.** Public classes with only static members, neither static nor sealed: parameter builders and a
  holder. All true. Their fix (make the class static) is breaking, so on API that already existed they do not block.
- **CA1028.** Six `sbyte` enums of two to eight values, re-judged against all three exceptions of the FDG4041 list.
  None is a flags enum or used for unmanaged interop. None falls under the space-saving exception either: each is a
  single auto-property of a class with only a few instances, not a field of a frequently instantiated
  structure, an array element or bulk-serialized data, and the class's size is rounded up to a DWORD anyway. All six
  are true reports of a CONSIDER rule (suggestion).
- **IDE0251.** Members of internal mutable structs (an enumerator, a parser, a buffer, a JSON position) that do not
  change the instance: all true.
- **FDG4032.** Public structs without `IEquatable<T>`: polly's readonly argument structs and its `Outcome<TResult>` and
  `ResiliencePropertyKey<TValue>`, serilog's `Alignment`, and a constants-only struct in internal-b. The rule text
  states no exception, so all are true warnings, though polly's argument structs are never compared.
- **No reports.** The other diagnostics report nothing on these samples; their tests cover the positive cases.
  FDG4027 does report two mutable structs in internal-b's dependencies, which the tool does not count.

## Chapter 5 (5.1 to 5.3) mappings and analyzers

Package 0.1.0 with the mappings of sections 5.1 to 5.3 and the new member design analyzers, enabled by the profile
globalconfigs (library for the open-source samples, application for the internal ones). Only the newly mapped SDK
diagnostics and the new analyzers are counted. Every report was checked, except FDG5003 (152; 20 checked across all
five samples) and FDG5010 (32; 20 checked).

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1024 | FDG5024 | 0 | 0 | 0 | 0 | 5 | 5 | 1 | 4 | FDG5024 lowered to suggestion. |
| CA1033 | FDG5022 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA1043 | FDG5035 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA1044 | FDG5027 | 0 | 0 | 0 | 0 | 6 | 6 | 0 | 6 | None needed. |
| CA1065 | FDG5031, FDG5054 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 1 | None needed (see notes). |
| CA1810 | FDG5055 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (already a suggestion). |
| CA1819 | FDG5025 | 0 | 0 | 0 | 0 | 6 | 6 | 0 | 6 | None needed. |
| CA2207 | FDG5055 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (already a suggestion). |
| CA2214 | FDG5052 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG5003 | FDG5003 | 115 | 16 | 9 | 6 | 6 | 20 | 0 | 20 | Narrowed before this run (see notes). |
| FDG5004 | FDG5004 | 0 | 0 | 2 | 0 | 1 | 3 | 0 | 3 | None needed. |
| FDG5005 | FDG5005 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG5008 | FDG5008 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 1 | None needed. |
| FDG5010 | FDG5010 | 12 | 0 | 0 | 0 | 20 | 20 | 0 | 20 | None needed. |
| FDG5011 | FDG5011 | 11 | 0 | 0 | 1 | 1 | 13 | 0 | 13 | None needed. |
| FDG5013 | FDG5013 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG5015 | FDG5015 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG5034 | FDG5034 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | None needed. |
| FDG5040 | FDG5040 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG5046 | FDG5046 | 0 | 1 | 0 | 0 | 9 | 10 | 0 | 10 | None needed. |

### Notes

- **polly's configuration.** polly's own globalconfig files set CA1024, CA1043, CA1810, CA1819 and CA2207 to `none`
  and take precedence over the package, so polly's zeros for those diagnostics measure polly's configuration.
- **FDG5003, narrowing.** A first run reported 177 parameters. Two groups follow rules the book states elsewhere and
  are left out: `CancellationToken` parameters kept last (section 9.2.2; 18 reports), and, in exception
  constructors, a leading `string message` and a trailing `Exception innerException`, where the common forms of
  section 7.4 put them (7 reports). The parameters between them are still compared; an earlier version skipped every
  pair of exception constructors where either started with `string message`, and the change did not alter the count
  on these samples. An intermediate run also left out overloads with different numbers of type parameters; the book
  allows no such exception, so that was undone and those reports (such as
  `Add(Func<Exception> generator, int weight)` beside `Add<TException>(int weight)`) are counted again.
- **FDG5003.** The 20 checked reports are all true: an overload that inserts a parameter (an exception, a level, a
  type, a context, a callback) in front of a parameter that the shorter overload has first. In serilog this is the
  shape of every logging method (`Write(level, messageTemplate)` beside `Write(level, exception, messageTemplate)`).
  internal-a has the same shape in six extension methods of one static
  class, and internal-b in three static methods of two helper classes and three constructors of one class.
  Reordering changes public signatures, so FDG5003 is `autofix: breaking`: on API that already exists the reports do
  not block, and new API is held to the rule.
- **FDG5004.** Three reports, all true: a public virtual method beside a longer public virtual overload, a public
  virtual parameterless method beside a protected virtual overload that it calls (overriders see both, so they get
  two virtual entry points; the pattern of the body makes the shorter one nonvirtual), and a virtual parameterless
  method beside a virtual generic overload that calls it.
- **FDG5008.** newtonsoft-json's `JsonConverter<T>` declares `WriteJson(writer, T? value, serializer)` beside
  `WriteJson(writer, object? value, serializer)`; when `T` is `object` the first cannot be called. True.
- **FDG5010.** The 20 checked reports (all 12 of serilog, 8 of internal-b) are methods and constructors with two to
  six default parameters and no overload that takes only the required ones. Some of serilog's methods have an overload
  that takes the required parameters and one of the defaulted ones; the body asks for an overload for users who need
  only the defaults, so these are true.
- **FDG5011.** Default values on interface members, on abstract methods and on the overrides that repeat them, and
  one on a virtual generic method. All true. FDG5011 now also reports an override of a member from a referenced
  assembly when the override adds or changes a default, and not when it repeats the overridden default; the count on
  these samples did not change.
- **FDG5046.** polly's `resilienceEvent` sets `Event`; internal-b has constructor parameters with short or prefixed
  names (`s`, an abbreviation, a leading underscore, a different word) that only set a property. All true.
- **CA1024.** Five parameterless `Get` methods in internal-b. Four return a constant or map a field through a
  `switch`, which a property may do. One computes its value and allocates on each call, which FDG5025 keeps a method
  (false positive). 1 of 5 is above 1 in 20, so FDG5024 is now a suggestion.
- **CA1065.** The one report is a `Dispose` method that throws `InvalidOperationException` when disposed out of
  order. It is outside FDG5031 (property getters) and FDG5054 (static constructors), but it is a true warning under
  section 9.4.1, which asks not to throw from `Dispose` except in critical situations. Both rules stay warnings.
  FDG5031 is `autofix: breaking` (the redesign the body asks for replaces the property with a method), so CA1065 is a
  breaking-fix diagnostic and its reports on existing API do not block.
- **CA1044 and CA1819.** Set-only compatibility properties of one class and array-typed auto-properties of model
  classes in internal-b. All true.
- **No reports.** The other diagnostics report nothing on these samples; their tests cover the positive cases.

## Chapter 5 (5.4 to 5.7) mappings and analyzers

Package 0.1.0 with the mappings of sections 5.4 to 5.7 and the new event, field, extension method and operator
analyzers, enabled by the profile globalconfigs (library for the open-source samples, application for the internal
ones). Only the newly mapped SDK diagnostics and the new analyzers are counted. Every report was checked, except CA1062
(56; 20 checked: all 9 of serilog and 11 of internal-b).

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1003 | FDG5057 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA1051 | FDG5065 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA1062 | FDG5073 | 9 | 0 | 0 | 0 | 47 | 20 | 0 | 20 | None needed. All 56 are true warnings under 5.8.2 (argument validation), counted separately; none is an FDG5073 instance (no `this` parameter). |
| CA1065 | FDG5092 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 1 | None needed (see notes). |
| CA1802 | FDG5066 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| CA2225 | FDG5087 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG5059 | FDG5059 | 1 | 0 | 2 | 0 | 8 | 11 | 0 | 11 | Library only after the cross-check (see notes). |
| FDG5060 | FDG5060 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | Library only after the cross-check (see notes). |
| FDG5061 | FDG5061 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5062 | FDG5062 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5063 | FDG5063 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5068 | FDG5068 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5074 | FDG5074 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5075 | FDG5075 | 1 | 1 | 2 | 0 | 0 | 4 | 0 | 4 | Narrowed before this run (see notes). |
| FDG5076 | FDG5076 | 0 | 12 | 0 | 0 | 0 | 12 | 0 | 12 | None needed. |
| FDG5077 | FDG5077 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5094 | FDG5094 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5096 | FDG5096 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5097 | FDG5097 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5099 | FDG5099 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |

### Notes

- **polly's configuration.** polly's own globalconfig files set CA1003, CA1051, CA1062, CA1802 and CA2225 to `none`
  and take precedence over the package, so polly's zeros for those diagnostics measure polly's configuration.
- **CA1062.** None of the 56 reports is an FDG5073 instance: none is on the `this` parameter of an extension method.
  They are parameters of public static and instance methods, constructors, a property setter and an operator,
  dereferenced without a null check: true warnings under the argument validation guidelines of section 5.8.2, counted
  separately as spec §7 asks, with 0 false positives. CA1062 stays mapped to FDG5073, the only rule of this round that
  it checks.
- **CA1065.** The same `Dispose` report as in the 5.1 to 5.3 run. It is outside FDG5092 (implicit conversion
  operators), and a true warning under section 9.4.1.
- **FDG5059.** Instance events of unsealed public classes with no protected virtual `On` method: serilog's level
  switch change event, two newtonsoft-json events (one raised from an internal nonvirtual `OnError`, one from no `On`
  method), and in internal-b six `PropertyChanged` events and two other change events of public classes. All true.
- **FDG5060.** internal-b has a protected nonvirtual `On` method without parameters for one of those change events.
  True.
- **FDG5059 and FDG5060, library only.** After the cross-check (Ruling 26) both are extensibility rules for public
  API, with `application: none`. The counts above are from the run before that change. A rerun with the application
  profile for the internal samples reports 0 for FDG5059 and FDG5060 on internal-a and internal-b (8 and 1 before);
  the library samples are unchanged (FDG5059: serilog 1, newtonsoft-json 2). FDG5077 still reports 0.
- **FDG5075, narrowing.** A first run reported 13 classes. Nine were polly classes named
  `[Feature][ExtendedType]Extensions` (one per resilience strategy), which name the class for its functionality and
  are no longer reported; the analyzer now reports only `Extensions` alone or after the name of a type that the class
  extends. The 4 reports left (serilog's `LoggerExtensions` on `ILogger`, polly's builder extensions class, and two
  newtonsoft-json classes named `Extensions`) are true.
- **FDG5076.** polly declares its strategy extension methods (`AddRetry`, `AddCircuitBreaker` and others) and one
  legacy helper in the root namespace, beside the builder and property types they extend, which are classes of the
  same assembly. None is an interface, a generic restriction or a dependency split, so all 12 are true.
- **No reports.** The other diagnostics report nothing on these samples; their tests cover the positive cases. Under
  Ruling 21 an analyzer with 0 reports passes the false positive gate.

## Chapter 5 (5.8 to 5.9) mappings and analyzers

Package 0.1.0 with the mappings of sections 5.8 and 5.9, the new parameter and tuple analyzers, and the FDG5011 change
(an override is compared with the default of a referenced member, not with a source parent), enabled by the
profile globalconfigs (library for the open-source samples, application for the internal ones). Only the newly
mapped SDK diagnostics, the new analyzers and FDG5011 are counted. Every report was checked, except CA1062 (56, the
same reports as in the 5.4 to 5.7 run, where 20 were checked).

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1021 | FDG5113 | 4 | 0 | 2 | 0 | 0 | 6 | 0 | 6 | None needed. |
| CA1045 | (FDG5113) | 0 | 0 | 0 | 0 | 3 | 3 | 3 | 0 | Not mapped after this run (see notes). |
| CA1062 | FDG5073, FDG5108, FDG5109, FDG5122 | 9 | 0 | 0 | 0 | 47 | 20 | 0 | 20 | None needed. True warnings under FDG5108 and FDG5109; none is an FDG5073 or FDG5122 instance (no `this` parameter, no params array). |
| CA1725 | FDG5104 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed. |
| FDG5011 | FDG5011 | 11 | 0 | 0 | 1 | 1 | 13 | 0 | 13 | Unchanged by the root comparison (see notes). |
| FDG5102 | FDG5102 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5103 | FDG5103 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5105 | FDG5105 | 0 | 0 | 2 | 0 | 0 | 2 | 0 | 2 | None needed. |
| FDG5111 | FDG5111 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 1 | None needed. |
| FDG5114 | FDG5114 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5115 | FDG5115 | 1 | 1 | 0 | 0 | 0 | 2 | 0 | 2 | None needed. |
| FDG5118 | FDG5118 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5123 | FDG5123 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5124 | FDG5124 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5128 | FDG5128 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5129 | FDG5129 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5130 | FDG5130 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5131 | FDG5131 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |
| FDG5132 | FDG5132 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 reports. |

### Notes

- **polly's configuration.** polly's own globalconfig files set CA1021, CA1045 and CA1062 to `none`, so polly's zeros
  for those diagnostics measure polly's configuration. They set CA1725 to `warning`, so its 0 there is a measurement.
- **CA1021.** serilog's `BindMessageTemplate` and `BindProperty`, each declared on the logger interface and on the
  static facade: they return bool with out parameters, the shape of the Try pattern without the Try prefix that
  section 7.5.2 asks for. newtonsoft-json's schema `IsValid` extension with an out list of error messages, and the
  serialization binder's `BindToName`, a void method with two out parameters. All 6 are true.
- **CA1045, narrowing.** All 3 reports in internal-b are the same protected helper, `Set<T>(ref T field, T newValue,
  string propertyName)`, which property setters of the property change notification pattern call. That pattern
  requires ref, the exception of the text, so the 3 are false positives, over 1/20. CA1045 reports every ref
  parameter and cannot tell such patterns apart, so it is no longer mapped: FDG5113 keeps CA1021 only, and FDG5114
  reports ref parameters of reference types (it does not report this helper, whose type parameter is unconstrained).
- **CA1062.** The same 56 reports as in the 5.4 to 5.7 run. They are dereferenced parameters of public methods,
  constructors, a property setter and an operator: true warnings under FDG5108 and FDG5109, which this round maps.
  None is on a params array, so none counts for FDG5122; none is on a comparison or hashing method, which the Mariani
  annotation of 5.8.2 would let skip validation.
- **FDG5011.** The counts match the 5.1 to 5.3 run, so comparing with a referenced member changed nothing on these
  samples; tests cover the cases it changes (a metadata, source, source chain, and two referenced levels).
- **Re-run after the cross-check.** With the fixes of the cross-check (FDG5102 leaves pointer parameters of members
  with a pointer-free overload, FDG5011 compares with the nearest overridden member declared in a referenced
  assembly, FDG5118 sees deconstruction assignments, the tuple rules see events), FDG5011, FDG5102, FDG5118 and
  FDG5128 to FDG5130 were measured again: FDG5011 11, 0, 0, 1, 1 as above, the others 0.
- **FDG5105.** newtonsoft-json's `DeserializeXmlNode` and `DeserializeXNode` overloads with the two bool parameters
  `writeArrayAttribute` and `encodeSpecialCharacters`. True.
- **FDG5111.** internal-a validates an enum constructor argument with `if (!Enum.IsDefined(typeof(T), value)) throw
  new ArgumentOutOfRangeException(...)`. True.
- **FDG5115.** serilog's public token constructor takes `in Alignment? alignment`, and polly's public abstract
  telemetry `Write` method takes its event arguments struct by `in`. Both are value types passed by read-only
  reference in public members. True.
- **No reports.** The other diagnostics report nothing on these samples; their tests cover the positive cases. Under
  Ruling 21 an analyzer with 0 reports passes the false positive gate. The library-only rules (FDG5102, FDG5115,
  FDG5123, FDG5124 and FDG5128 to FDG5132) are `none` in the application profile, so the internal samples measure them
  only by their absence.

## Chapter 6 mappings and analyzers

Package 0.1.0 with the mappings of chapter 6 and its two analyzers, enabled by the profile globalconfigs (library for
the open-source samples, application for the internal ones). Chapter 6 maps no SDK diagnostic, so only FDG6013 and
FDG6019 are counted. FDG6013 was checked at 20 evenly spaced locations, FDG6019 in full.

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| FDG6013 | FDG6013 | 3 | 8 | 191 | 0 | 0 | 20 | 0 | 20 | None needed (202 reports in total). |
| FDG6019 | FDG6019 | 0 | 1 | 0 | 0 | 0 | 1 | 0 | 1 | None needed. |

### Notes

- **FDG6013.** Every checked report is a public virtual or abstract method, property or indexer declared (not
  overridden) in a public class: serilog's abstract `Render` and `Length` of its property value and token classes, and
  newtonsoft-json's reader, writer, serializer, token and naming strategy classes, whose public virtual `Read...`,
  `Write...` and settings members are the extensibility points. polly's 8 are public abstract or virtual members of
  its context pool, pipeline provider and telemetry listener classes and of its rejection exception. The body asks
  for protected virtual members called from public ones, so all are true warnings; none is an override. The 11
  reports on newtonsoft-json's token class stay true although its only constructor is internal: third parties can
  still override the public virtual members of its public subclasses, as in the Animal and Plant example of 6.3.
- **FDG6019.** polly's pipeline builder base class, which the type parameter constraints of public extension methods
  name, so callers meet the Base suffix in those APIs. True. newtonsoft-json's date and time converter base class is
  not reported: the compilation uses it only as a base class. A first run that did not read constraints reported
  nothing; constraints were added to the analyzer after it. The second run counts a class only when it is a base
  class (abstract, with a protected or virtual member that is not an override, or derived from in the compilation,
  generated code excluded); the polly class is abstract, so the count is unchanged.
- **Library only.** Both rules are `none` in the application profile, so the internal samples measure them only by
  their absence.

## Chapter 7 mappings and analyzers

Package 0.1.0 with the mappings of chapter 7 and its ten analyzers, enabled by the profile globalconfigs (library for
the open-source samples, application for the internal ones). The mapped SDK diagnostics and the new analyzers are
counted. Every report was checked, except CA1062, whose reports are the 56 of the chapter 5 (5.8 to 5.9) section.

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1031 | FDG7025, FDG7026, FDG7033, FDG7038, FDG7039 | 11 | 0 | 0 | 3 | 6 | 20 | 0 | 20 | None needed. |
| CA1032 | FDG7059 | 1 | 0 | 0 | 2 | 0 | 3 | 0 | 3 | None needed. |
| CA1058 | FDG7040 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1510 | FDG7009 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports; no sample targets .NET 6 or later). |
| CA1511 | FDG7009 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports; no sample targets .NET 7 or later). |
| CA1512 | FDG7009 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports; no sample targets .NET 8). |
| CA1513 | FDG7009 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports; no sample targets .NET 7 or later). |
| CA1062 | FDG7045 | 9 | 0 | 0 | 0 | 47 | - | - | - | None needed. Counted in the chapter 5 (5.8 to 5.9) section; true warnings under FDG5108. |
| CA1710 | FDG7057 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA2200 | FDG7031 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA2201 | FDG7018, FDG7037, FDG7040, FDG7045, FDG7046, FDG7048, FDG7049 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA2208 | FDG7019, FDG7043 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA2219 | FDG7011 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA2237 | FDG7058 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports; see the notes). |
| FDG7007 | FDG7007 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG7008 | FDG7008 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG7010 | FDG7010 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG7021 | FDG7021 | 3 | 0 | 3 | 2 | 0 | 8 | 0 | 8 | None needed. |
| FDG7022 | FDG7022 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG7036 | FDG7036 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG7044 | FDG7044 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG7047 | FDG7047 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG7051 | FDG7051 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG7064 | FDG7064 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 1 | Narrowed after the first run (8 reports, 4 FP); see the notes. |

### Notes

- **CA1031.** Every report is a catch of Exception, or a parameterless catch, that does not rethrow. serilog's 11 are
  in its logger, sinks, enrichers and formatter: 5 write the failure to its self-log or build a fallback string, and 6
  pass the exception to a failure listener and go on. internal-a's 3 log and continue, return null, or have an empty
  block; internal-b's 6 are empty catches or return a default value. All swallow errors, so all are true warnings
  under FDG7025 (library profile) or FDG7026 (application profile). One internal-a catch logs and continues inside an
  event loop, which could be read as the top-level handler that FDG7039 allows; it stays a true FDG7026 warning, since
  the loop goes on after an unknown exception. None transfers the exception to another thread.
- **CA1032.** serilog's 1 and internal-a's 2 are exception types without the (), (string) and (string, Exception)
  constructors. True.
- **CA1062.** The same 56 reports as in the chapter 5 (5.8 to 5.9) section, where they are true warnings under
  FDG5108. For FDG7045, which is library only, only serilog's 9 count; internal-b's 47 come from the application
  profile of FDG5108.
- **FDG7021.** All 8 are constant or interpolated messages whose last sentence ends without a period (serilog's 3 in
  its settings parser, newtonsoft-json's 3 in an object id, a converter and a reflection helper, and internal-a's 2).
  True. No message ends with a question mark or exclamation point, and FDG7022 reports none.
- **FDG7064.** The first run reported every Try method that does not return bool: 7 in polly and 1 in newtonsoft-json.
  4 were not Try methods of 7.5.2 (best-effort methods that return void or ValueTask, and a fluent method that returns
  its argument), so 4 of 8 were false positives. The analyzer was narrowed to Try methods with an out parameter; the
  second run reports only newtonsoft-json's internal conversion method, which returns an enum and an out value. True.
  polly's other 3 first-run reports, which return a nullable value or a tuple instead of an out parameter, were true
  and are no longer seen; the coverage stays partial.
- **Zero reports.** polly's 0 for every diagnostic is measured with its own configuration: its globalconfigs set
  CA1032, CA1062 and CA2237 to none and CA1710 to silent in some of its projects, and its code has no report for the
  diagnostics it keeps at warning. The 0-report analyzers pass gate 2 (Ruling 21). In the samples, FDG7036 finds no
  exception created in a catch block without the caught one as its inner exception.
- **Fix round 1 (rerun).** The package was rebuilt with the FDG7008 change (Task<T> and ValueTask<T> returns are
  unwrapped), the FDG7036 change (an exception passed in an array or an IEnumerable<T> parameter, as for
  AggregateException, is an inner exception) and the new mappings of FDG7009 (CA1510 to CA1513, suggestion) and
  FDG7058 (CA2237). Every count above is unchanged; FDG7008 and FDG7036 still have 0 reports. CA1510 to CA1513 report
  only when the target framework has the throw helpers, and every sample builds for netstandard2.0 or net472, so they
  cannot report here. CA2237 reports only on .NET Framework; the internal samples build for net472 and report none,
  and polly turns it off.

## Chapter 8 mappings and analyzers

Package 0.1.0 with the mappings of chapter 8 and its six analyzers (21 diagnostics), enabled by the profile
globalconfigs (library for the open-source samples, application for the internal ones). The mapped SDK diagnostics
and the new analyzers are counted. Every report was checked; the 133 CA2227 reports and the 86 CA1056 reports of the
internal samples were checked by their shape, which is the same in each. The counts of FDG8015, FDG8026, FDG8053 and
FDG8096 are those of the rerun after fix round 1.

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1002 | FDG8014 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 1 | None needed. |
| CA1010 | FDG8034 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1018 | FDG8006 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1019 | FDG8008, FDG8009, FDG8010 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1036 | FDG8058 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1046 | FDG8100 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1054 | FDG8091 | 0 | 0 | 0 | 6 | 2 | 8 | 0 | 8 | None needed. |
| CA1055 | FDG8091 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | None needed. |
| CA1056 | FDG8091 | 0 | 0 | 0 | 4 | 82 | 86 | 0 | 86 | None needed. |
| CA1058 | FDG8037, FDG8098 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1065 | FDG8066, FDG8075, FDG8083, FDG8101 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 1 | None needed (see notes). |
| CA1066 | FDG8065, FDG8068 | 1 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | None needed. |
| CA1067 | FDG8055 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1710 | FDG8005, FDG8038, FDG8039 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1813 | FDG8012 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1814 | FDG8004 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1815 | FDG8067 | 1 | 0 | 0 | 0 | 1 | 2 | 1 | 1 | FDG8067 is now a suggestion; see the notes. Counted per struct. |
| CA1819 | FDG8003, FDG8032 | 0 | 0 | 0 | 0 | 6 | 6 | 0 | 6 | None needed. |
| CA2227 | FDG8020 | 0 | 0 | 0 | 0 | 133 | 133 | 0 | 133 | None needed (see notes). |
| CA2231 | FDG8105 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA2234 | FDG8094 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8002 | FDG8002 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8011 | FDG8011 | 0 | 3 | 5 | 0 | 0 | 8 | 0 | 8 | None needed (see notes). |
| FDG8014 | FDG8014 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8015 | FDG8015 | 1 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | None needed. |
| FDG8016 | FDG8016 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8017 | FDG8017 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8026 | FDG8026 | 0 | 1 | 11 | 0 | 0 | 12 | 0 | 12 | Narrowed after the first run (15 reports, 3 FP); see the notes. |
| FDG8031 | FDG8031 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | None needed. |
| FDG8050 | FDG8050 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 1 | None needed. |
| FDG8051 | FDG8051 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8053 | FDG8053 | 3 | 34 | 4 | 0 | 0 | 41 | 0 | 41 | Narrowed after the first run (43 reports, 0 FP); see the notes. |
| FDG8057 | FDG8057 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8070 | FDG8070 | 0 | 0 | 2 | 0 | 0 | 2 | 0 | 2 | None needed. |
| FDG8082 | FDG8082 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 1 | None needed. |
| FDG8088 | FDG8088 | 0 | 1 | 5 | 0 | 0 | 6 | 0 | 6 | None needed. |
| FDG8089 | FDG8089 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8096 | FDG8096 | 0 | 0 | 7 | 0 | 0 | 7 | 0 | 7 | None needed; the message no longer shows `?` (see notes). |
| FDG8102 | FDG8102 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8103 | FDG8103 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8104 | FDG8104 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG8106 | FDG8106 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |

### Notes

- **CA1002.** newtonsoft-json's 1 is an overridable method of its contract resolver that returns `List<T>`. True under
  FDG8014, which asks for collection types in public APIs (library only).
- **CA1054, CA1055 and CA1056.** Every report is a string parameter, return value or property whose name holds "url"
  or "uri": in internal-a, 6 parameters and 4 properties; in internal-b, 2 parameters, 1 return value and 82
  properties of data model classes. True under FDG8091 (DO use System.Uri), whose application profile keeps the
  warning; the test profile turns these diagnostics off.
- **CA1065.** The one report is the same `Dispose` method of internal-a as in the chapter 5 sections. It is outside
  FDG8066, FDG8075, FDG8083 and FDG8101 (Equals, GetHashCode, ToString and the equality operators), and a true warning
  under section 9.4.1. After the cross-check the four rules have test profile none, so CA1065 is off again in the test
  profile: it also reports the stub getters of test fakes that throw NotImplementedException, which belong to chapter 5,
  off in that profile.
- **CA1066.** serilog's 1 is a struct that overrides Equals without `IEquatable<T>`. True.
- **CA1815.** serilog's 1 is a public struct that does not override Equals. True. internal-b's 1 is a struct that holds
  only const fields, and CA1815 gives such a struct two messages (probe with SDK 10.0.402). "Should override Equals" is
  true by the letter: the text has no exception for it. "Should override the equality operators" is a false positive:
  FDG8105 asks for the operators only if equality is meaningful, and all values of the struct are equal. 1 false
  positive of 2 is above 1 in 20, and CA1815 cannot be narrowed to its Equals message, so FDG8067 is a suggestion in
  every profile. FDG8053 (warning) reports structs with an instance field that do not implement `IEquatable<T>`.
- **CA1819.** internal-b's 6 are properties of array type. True under FDG8003 and FDG8032. The test profile turns CA1819
  off (Ruling 32): test classes are public only for the test framework.
- **CA2227.** internal-b's 133 are `{ get; set; }` properties of type `List<T>` or `ObservableCollection<T>` in data
  model classes. True under FDG8020 (DO NOT provide settable collection properties); the rule stays a warning in the
  library and application profiles, and the test profile turns it off (Ruling 32).
- **FDG8011.** polly's 3 are attribute classes in its polyfill folder that copy attributes of newer frameworks;
  newtonsoft-json's 5 are its serialization attributes. Each declares more than one public constructor, so all are
  true. polly's copies must keep the constructors of the attributes they copy, so a suppression is its fix.
- **FDG8015.** serilog's 1 is a public method that takes a `Dictionary<TKey, TValue>`. True.
- **FDG8026.** The first run had 15 reports: polly's 1 (a helper that returns null as a nullable `string[]`, with a
  suppression of the SonarAnalyzer rule for the same guideline) and newtonsoft-json's 14. Of these, 11 return null as
  `byte[]` from its readers' `ReadAsBytes` methods and their async forms. True. 3 return null as the JSON property
  node type, which implements `IList<T>` of its children but is a node of a document tree, for which null means "not
  found". 3 of 15 is above 1 in 20, so in fix round 1 the analyzer was narrowed to arrays, `IEnumerable`,
  `IEnumerable<T>`, and types that implement a collection interface and are, or derive from, a type of the
  System.Collections namespaces. The rerun reports the 12 true ones and none of the 3. A collection class that
  implements the interfaces without such a base type is no longer seen; the coverage stays partial.
- **FDG8031.** internal-b's 1 is a `List<byte>` in the signature of a public member. True.
- **FDG8050.** newtonsoft-json's 1 is its token base class, which implements `ICloneable`. True.
- **FDG8053.** The first run had 43 reports: serilog's 3, polly's 35, newtonsoft-json's 4 and internal-b's 1.
  internal-b's 1 is the struct with only const fields of CA1815: true by the letter, but left out as a coverage choice:
  all its values are equal. The first run had 0 false positives in 43; in fix round 1 the analyzer was narrowed to
  structs with an instance field. The rerun reports 41: internal-b's struct and one empty struct of polly are no longer
  reported. polly's 34 are structs in files that already carry `#pragma warning disable CA1815`, so its maintainers
  decided against equality on them; most carry the arguments of its callbacks and events, but not all (an outcome type,
  a property key and an event type are among them). The guideline has no exception for them, so they are true by the
  letter, and adding FDG8053 to those pragmas is the fix. serilog's 3 and newtonsoft-json's 4 are public structs without
  the interface. True.
- **FDG8070.** newtonsoft-json's 2 are classes that override Equals and have mutable instance fields (its value token
  and its naming strategy). True.
- **FDG8082.** newtonsoft-json's 1 is the `ToString` override of its value token, which returns `string.Empty` for a
  null value. True.
- **FDG8088.** polly's 1 is a private serialization constructor of an unsealed class; newtonsoft-json's 5
  are public serialization constructors. True.
- **FDG8096.** newtonsoft-json's 7 are public methods of its static entry-point class that take or return `XmlNode`
  or `XmlDocument`. True. The first run showed the nullable annotation in the message (`'XmlNode?'`); fix round 1
  removed it, and the rerun has the same 7 reports with the plain type name.
- **Zero reports.** polly's 0 for every SDK diagnostic is measured with its own configuration: its library
  globalconfig keeps all of them at warning (CA2231 at error), and its library code has no report; its test and
  benchmark globalconfigs turn several off. The 0-report analyzers and mappings pass gate 2 (Ruling 21).
- **Fix round 1 (rerun).** The package was rebuilt with the FDG8026 and FDG8053 narrowings and the message change of
  the signature analyzer (no top-level nullable annotation in the type names of FDG8015, FDG8016, FDG8031 and
  FDG8096), and FDG8015, FDG8026, FDG8053 and FDG8096 were measured again. FDG8015 and FDG8096 are unchanged; FDG8026
  went from 15 to 12 and FDG8053 from 43 to 41. FDG8067 became a suggestion after the run, which changes no count.
- **Cross-check fixes (no rerun).** FDG8002 no longer reports a field initialized with an empty array, and FDG8031 no
  longer reports `ImmutableArray<byte>`. Both changes only remove reports: FDG8002 had none, and the one FDG8031 report
  is a `List<byte>`, which is still reported, so the counts stand. The internal-b struct of FDG8053 is recounted as true
  (see above), which changes no count of the rerun.

## Chapter 9 (9.1 to 9.3) mappings and analyzers

Package 0.1.0 with the mappings of sections 9.1 to 9.3 and four analyzers (21 diagnostics), enabled by the
profile globalconfigs (library for the open-source samples, application for the internal ones). The mapped SDK
diagnostics and the new analyzers are counted. Every report was checked. The counts are those of the rerun after
the narrowing of FDG9020, FDG9022 and FDG9037; the first-run counts are in the Treatment column and the notes.

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1068 | FDG9024 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1849 | FDG9040, FDG9041 | 0 | 0 | 3 | 0 | 0 | 3 | 0 | 3 | None needed (see notes). |
| CA2007 | FDG9039, FDG9050 | 1 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | None needed; library only (see notes). |
| CA2012 | FDG9042 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9020 | FDG9020 | 0 | 2 | 0 | 0 | 0 | 2 | 0 | 2 | Narrowed after the first run (6 reports, 4 FP); see the notes. |
| FDG9022 | FDG9022 | 2 | 6 | 0 | 11 | 0 | 19 | 0 | 19 | Narrowed after the first run (24 reports, 5 FP); see the notes. |
| FDG9023 | FDG9023 | 0 | 0 | 4 | 2 | 0 | 6 | 0 | 6 | None needed (see notes). |
| FDG9027 | FDG9027 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9028 | FDG9028 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9029 | FDG9029 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9033 | FDG9033 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9036 | FDG9036 | 0 | 0 | 2 | 0 | 0 | 2 | 0 | 2 | None needed (see notes). |
| FDG9037 | FDG9037 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | Narrowed after the first run (2 reports, 2 FP); see the notes. |
| FDG9038 | FDG9038 | 0 | 1 | 0 | 0 | 0 | 1 | 0 | 1 | None needed (see notes). |
| FDG9043 | FDG9043 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 1 | None needed. |
| FDG9045 | FDG9045 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9047 | FDG9047 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9048 | FDG9048 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9049 | FDG9049 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9052 | FDG9052 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9053 | FDG9053 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9054 | FDG9054 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9055 | FDG9055 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9056 | FDG9056 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9057 | FDG9057 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |

### Notes

- **FDG9020 and FDG9022 (narrowed).** In the first run, 4 of polly's 6 FDG9020 reports, with 4 of its 10
  FDG9022 reports, were static methods that return a completed `ValueTask` (`new(result)`) as a shared
  result, the way `Task.FromResult` does. Section 9.2.2 describes the "-Async" method as the one "which initiates
  an asynchronous operation", and these start none, so a static method without the async keyword whose every
  return value is a completed task is no longer reported by either. The same change removed 1 of internal-a's 12
  FDG9022 reports, a static setup method that returns a completed task. Positive test:
  `Static_method_returning_only_completed_tasks_is_not_reported`.
- **FDG9020 (rerun).** polly's 2 are the protected abstract template method ExecuteCore of its two strategy
  base classes, generic and not. True: section 9.2.10 names the combined suffix "AsyncCore".
- **FDG9022 (rerun).** serilog's 2 are batch-sink interface methods without a token. polly's 6 are strategy
  methods that receive the token inside a context object rather than as a parameter. internal-a's 11 are
  public interface and instance methods, none of which accepts a token. All are true by the letter of the rule,
  which asks for the parameter.
- **FDG9023.** newtonsoft-json's 4 are protected virtual writer methods whose token has no default value.
  internal-a's 2 are public methods whose token is required. True: FDG5011 bans default values on virtual and
  interface methods for every parameter type other than `CancellationToken`, so the virtual methods have no
  reason to leave the default out.
- **FDG9036.** newtonsoft-json's 2 are a cancellation helper that returns null, by its own convention, when
  cancellation is not requested. True.
- **FDG9037 (narrowed).** In the first run, both reports were newtonsoft-json's polyfill of `Task.FromCanceled`,
  which returns `new Task(action, cancellationToken)` with a token that is already canceled. Such a task starts
  in the Canceled state, not Created, so a constructor call that passes a `CancellationToken` is no longer
  reported. Positive test: `Task_created_with_a_cancellation_token_is_not_reported`.
- **FDG9038.** polly's 1 returns an outcome holding an `OperationCanceledException` when cancellation is
  requested, instead of throwing it. True by the letter: polly returns failures as values by design, which a
  suppression records.
- **FDG9043.** internal-a's 1 is a null check that throws `ArgumentNullException` inside a public async method,
  before its first await. True.
- **CA1849.** newtonsoft-json's 3 are two reads of `Task<TResult>.Result` after an `IsCompletedSuccessfully`
  check and one synchronous write call in an async method. True by the letter of FDG9040 and FDG9041; the
  `Result` reads cannot block, which a suppression records.
- **CA2007.** serilog's 1 is an await without `ConfigureAwait`. True. CA2007 (FDG9039, FDG9050), FDG9028 and
  FDG9048 are library only, so they did not run on the internal samples, which use the application profile.
- **CA1068.** 0 reports. CA1068 also reports synchronous methods, for which FDG9026 says the last-parameter
  guidance does not apply; none was seen in these samples.
- **internal-b.** No report in the measured project.
- **Fix round 1.** The analyzers of FDG9027, FDG9028, FDG9029 (now also checking methods that return
  `IAsyncEnumerable<T>`), FDG9043 (no longer reporting a throw after an await or await foreach in the text of the body,
  or after the end of an await using statement, as narrowed in fix round 2) and FDG9054 and FDG9057 (a SetValue call
  whose value is not the value parameter moved from FDG9054 to FDG9057) changed. A rerun of these six diagnostics gave
  the same counts as the table: internal-a's FDG9043 report stays, and the others have 0 reports. The FDG9043 rerun of
  fix round 2 gave the same 1.

## Chapter 9 (9.4 to 9.8) mappings and analyzers

Package 0.1.0 with the mappings of sections 9.4 to 9.8 and three analyzers (13 diagnostics), enabled by the
profile globalconfigs (library for the open-source samples, application for the internal ones). The mapped SDK
diagnostics and the new analyzers are counted. Every report was checked.

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1001 | FDG9062 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| CA1010 | FDG9093 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); shared with FDG8034. |
| CA1063 | FDG9062, FDG9064, FDG9065, FDG9066, FDG9074 | 0 | 0 | 0 | 0 | 2 | 2 | 0 | 2 | None needed (see notes). |
| CA1065 | FDG9069, FDG9076 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 1 | None needed (see notes). |
| CA1816 | FDG9065 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 | None needed (see notes). |
| CA2007 | FDG9083 | 1 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | None needed; the report belongs to FDG9039 (see notes); library only. |
| CA2213 | FDG9062 | 1 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | None needed (see notes). |
| FDG9067 | FDG9067 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9073 | FDG9073 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9074 | FDG9074 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9081 | FDG9081 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9082 | FDG9082 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9100 | FDG9100 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9103 | FDG9103 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9106 | FDG9106 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9111 | FDG9111 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9112 | FDG9112 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9115 | FDG9115 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9116 | FDG9116 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9117 | FDG9117 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |

### Notes

- **CA1063 and CA1816.** internal-b's 3 reports are one public unsealed disposable type: CA1063 asks for an
  overridable Dispose(bool) (FDG9064) and for a Dispose() that only calls Dispose(true) and then
  GC.SuppressFinalize (FDG9065), and CA1816 reports the missing GC.SuppressFinalize call (FDG9065). True.
- **CA1065.** internal-a's 1 report is an `InvalidOperationException` created in the Dispose() of a public
  type. True by the shape of FDG9069 (users expect Dispose not to throw); whether the throw is one of the
  critical situations that the rule excepts is left to review, as the notes of FDG9069 say.
- **CA2007.** serilog's 1 report is a plain await of a task in a batching sink, not an await using, so it
  belongs to FDG9039 (it is the same report as in the 9.1 to 9.3 section) and is not counted as true for
  FDG9083. Not a false positive of CA2007 either.
- **CA2213.** serilog's 1 report is a `CancellationTokenSource` field that the batching sink creates and
  never disposes. True for FDG9062.
- **New analyzers.** The 13 diagnostics of DisposePatternAnalyzer, QueryPatternAnalyzer and VarianceAnalyzer
  have 0 reports, so they pass gate 2 (Ruling 21). FDG9073, FDG9106 and the variance rules FDG9111, FDG9112,
  FDG9115 and FDG9116 are off in the application profile, so only the open-source samples are measured for
  them. serilog and polly declare no public generic interface or delegate; newtonsoft-json declares two, an
  array pool whose type parameter is in a returned array and in a parameter (both ways, so neither FDG9112 nor
  FDG9116 applies) and a token sequence interface that is already covariant and returns no array.
- **Rerun after the cross-check and review.** FDG9082 looks only at methods named DisposeAsync, FDG9103 no
  longer reports an OrderBy that returns its own type when that type is already an ordered sequence type, and the
  variance rules count the members of nested interfaces and delegates. Measured again for FDG9067, FDG9082,
  FDG9103, FDG9111, FDG9112, FDG9115 and FDG9116 with the same samples and profiles: still 0 reports for each, so
  the table does not change.
- **Second rerun.** FDG9067 reports extern Dispose overloads again (Ruling 36), FDG9103 finds ThenBy through
  implemented interfaces too and keeps reporting an OrderBy that returns IEnumerable<T>, and the variance rules
  skip an interface with a class, struct or enum in a nested interface (CS8427). Measured again for FDG9067,
  FDG9103, FDG9112 and FDG9116: still 0 reports for each.

## Chapter 9 (9.9 to 9.12) mappings and analyzers

Package 0.1.0 with the mappings of sections 9.9 to 9.12 and three analyzers (16 diagnostics), enabled by the
profile globalconfigs (library for the open-source samples, application for the internal ones). The mapped SDK
diagnostic and the new analyzers are counted. There was no report to check.

| Diagnostic | Rules | serilog | polly | newtonsoft-json | internal-a | internal-b | Checked | FP | True | Treatment |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| CA1844 | FDG9149 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports); library only. |
| FDG9130 | FDG9130 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9138 | FDG9138 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9142 | FDG9142 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9143 | FDG9143 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9144 | FDG9144 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9147 | FDG9147 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9148 | FDG9148 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9156 | FDG9156 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9157 | FDG9157 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9159 | FDG9159 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9162 | FDG9162 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9163 | FDG9163 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9171 | FDG9171 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9172 | FDG9172 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9173 | FDG9173 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |
| FDG9174 | FDG9174 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | None needed (0 reports). |

### Notes

- **CA1844.** FDG9149 is library only, so only the open-source samples are measured for it. None of them
  declares a class derived from Stream that overrides the array-based ReadAsync or WriteAsync.
- **New analyzers.** The 16 diagnostics of TimeoutAnalyzer, SpanParameterAnalyzer and BufferSignatureAnalyzer
  have 0 reports, so they pass gate 2 (Ruling 21). The open-source samples declare no public parameter or
  property of an integer type named as a timeout, their public span parameters are ReadOnlySpan<T> (one sample,
  in synchronous methods and a params parameter), and no sample declares a method that returns
  OperationStatus or a Try-Write method with a parameter named destination. The samples exercise these rules
  little, so the 0 reports say more about the absence of the shapes than about precision; the precision rests
  on the unit tests, which include the compliant examples of the book.
- **Rerun after the cross-check and review.** FDG9130 accepts nanosecond and Millis unit words and reports a
  positional record property once, FDG9138 counts a deconstruction target as a write and skips throw-only
  bodies, FDG9156 checks a string or array source only when the method has an output buffer, and FDG9159 and
  FDG9172 no longer exempt a byte buffer counted by chars (Ruling 37). Measured again for FDG9130, FDG9138,
  FDG9156, FDG9159 and FDG9172 with the same samples and profiles: still 0 reports for each, so the table does
  not change.
- **Second rerun for FDG9156.** Beside a string or array source, only a Span<T> or Memory<T> parameter, or a
  non-params array or ArraySegment<T> named destination or with byte or char elements, counts as an output buffer
  (Ruling 40). Measured again with the same samples and profiles: still 0 reports, so the table does not change.

## Appendix D mappings

Appendix D (Breaking Changes, FDGD001 to FDGD063) maps no SDK diagnostic and adds no analyzer: all 63 rules are
judgment rules about changes to a shipped public API, which only a comparison with a baseline can show. The
PublicApiAnalyzers RS diagnostics are not mapped (design §8a). No new diagnostics, so nothing was measured.
