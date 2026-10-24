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
| FDGA004 | FDGA004 | 110 | 0 | 0 | 6 | 94 | 20 | 0 | 20 | R6: this project's analyzer, which exempts argument validation preambles (FDGA005), replaces IDE0011. |
| FDGA023 | FDGA023 | 141 | 89 | 989 | 20 | 84 | 20 | 0 | 20 | R6: narrowed so that an enclosing operator expression no longer makes a list long. Under the threshold after the narrowing. |
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
  added none. A fresh spot check of 20 found no false positives. Lines where the operator is inside the list itself (43
  reports, for example `F(a ?? b, c, d)`) are still reported, because the list is what makes the line long. About 12
  newtonsoft-json reports are lists that are chopped one item per line but whose closing parenthesis is on its own
  line. The rule text does not say where the parenthesis goes, but the rule's example keeps it on the last item's
  line, so these are counted as true warnings; they are debatable.
- **FDGA004 (was IDE0011).** IDE0011 first counted serilog 123, polly 0, newtonsoft-json 0, internal-a 28 and
  internal-b 98, and 5 of 20 spot-checked reports were false positives: all an argument validation preamble
  (`if (x == null) throw ...;`), which FDGA005 allows without braces. Lowering IDE0011 to `suggestion` (R7) would have
  dropped its fix from `dotnet fdg fix` for the 95% of reports that are true warnings, so R6 applies instead: the
  FDGA004 analyzer reports what IDE0011 reports and exempts the `if`...`throw` statements without `else` at the start
  of a member, accessor, local function or lambda body, up to the first blank line or other statement. FDGA004 counts
  serilog 110, internal-a 6 and internal-b 94, and the 20 spot-checked reports are all true warnings: their embedded
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
