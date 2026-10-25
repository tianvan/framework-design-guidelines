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
| CA1720 | FDG3016, FDG3017, FDG3018 | 0 | 0 | 0 | 0 | 1 | 1 | 1 | 0 | Over the threshold (n = 1). Already `suggestion` in every profile; no change. |
| CA1724 | FDG3032 | 0 | 0 | 0 | 0 | 3 | 3 | 2 | 1 | Over the threshold (n = 3). No change yet; see the notes. |
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
  FDG3032 text covers only "a type in that namespace". The sample is too small for a decision, and FDG3032's notes
  deliberately include framework namespaces, so the mapping is left unchanged.
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
