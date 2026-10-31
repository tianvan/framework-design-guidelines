# Performance

How long the plugin's hooks take on real code.

## Stop time

The Stop hook (`dotnet-fdg hook turn-stop`) checks the files the AI changed in a turn: it formats them, builds their
projects and reports the diagnostics on the changed lines. Below are the **baseline of 0.2.0**, measured before any
change to the hook, and the numbers **after the Stop changes** of this release cycle, with a re-measure of 0.2.0 in the
same window.

### Method

`eng/perf/Measure-Stop.ps1` measures on a temporary copy of an open-source sample from `eng/noise/samples.json`,
fetched at its pinned commit into `<WorkRoot>/perf/<sample>`, as `eng/noise/Measure-Noise.ps1` fetches samples. Each
run resets the copy to the pinned commit and onboards every project the script can change in that sample:

- a `PackageReference` to the measured package with `PrivateAssets="all"`; polly uses central package management, so
  there the version goes into its `Directory.Packages.props`;
- `FrameworkDesignGuidelinesProfile` set to the sample's profile, `library`;
- `TargetFrameworks` set to the sample's framework, `netstandard2.0`, the one framework `Measure-Noise.ps1` builds.
  serilog targets seven frameworks on Windows and polly five; with one framework the Stop builds once per project;
- `TreatWarningsAsErrors` set to `false`, as `Measure-Noise.ps1` does: serilog sets it to `true`, and the package's
  warnings on existing code would fail the build.

The directory that holds the package is added as a package source to the copy's root `NuGet.config` (with a source
mapping for the package in polly, which maps its sources). The onboarding is committed in the copy, so the hook sees
only the turn's change. `NUGET_PACKAGES` is `<WorkRoot>/perf/packages/<package file name without extension>`. NuGet
keeps an extracted package by id and version only, so before the first build the script deletes a cached copy of the
same id and version whose SHA-512 differs from the measured package file.

After one full build of the onboarded projects, every round:

1. runs `hook turn-start` with the recorded Claude payload `tests/fixtures/hooks/claude/user-prompt-submit.json`, its
   `cwd` and `session_id` replaced;
2. adds this method before the closing brace of the class in one file per changed project:

   ```csharp
       public static string Probe(int attempt, string key)
       {
           try { return key.Trim(); } catch (Exception ex) { return ex.Message; }
       }
   ```

3. times `hook turn-stop` with `tests/fixtures/hooks/claude/stop.json` (same replacements), from process start to exit;
4. restores the copy with `git checkout -- .` and `git clean -fd`.

The first round is a warm-up and is not counted. In the warm cases the build servers (MSBuild nodes and the compiler
server) stay up between rounds; in the cold case (`-Cold`) `dotnet build-server shutdown` runs before every Stop. That
command stops the build servers of the whole machine, not only those of the sample. The CLI runs as
`dotnet dotnet-fdg.dll`. `FDG_HOOK_TIMEOUT_SECONDS` is 600, so that a slow Stop is measured instead of cut off at the
default 60 s; the other hook settings keep their defaults. A round fails, instead of being counted, when the Stop
reports an error, a timeout or an unchecked project, leaves a changed file as written, or writes fewer SARIF logs than
the projects it changed. The time and hook output of every round, warm-up included, are written to
`<WorkRoot>/perf/logs`.

### Samples and changes

| Case | Sample | Commit | Changed project | Changed file |
| --- | --- | --- | --- | --- |
| One project | serilog | `81af75f3a5e28ec9f5be18ef74357a9914a51745` | `src/Serilog/Serilog.csproj` | `src/Serilog/Capturing/PropertyBinder.cs` |
| Two projects | polly | `3f3adad9d5af85658fc3585018f02a2d8c2173e8` | `src/Polly.Core/Polly.Core.csproj` | `src/Polly.Core/Timeout/TimeoutResilienceStrategy.cs` |
| | | | `src/Polly.Extensions/Polly.Extensions.csproj` (references Polly.Core) | `src/Polly.Extensions/Telemetry/TelemetryUtil.cs` |

For `-Projects 3`, the script also changes `src/Polly.RateLimiting/RateLimiterResilienceStrategy.cs` in
`src/Polly.RateLimiting/Polly.RateLimiting.csproj`, which references Polly.Core.

What the Stop does in these rounds:

- **serilog.** `PropertyBinder.cs` is formatted in full: the catch is expanded, and the existing code gets explicit
  access modifiers, braces, explicit types and wrapped long lines. The build reports CA1031 on the added
  `catch (Exception ex)`, so the Stop blocks with one issue.
- **polly.** Both files are formatted: the catch is expanded, and two long signatures in
  `TimeoutResilienceStrategy.cs` are wrapped. The Stop reports nothing.

The one-project and two-project cases use different samples, so the difference between them is not only the second
project.

### Machine

| | |
| --- | --- |
| Operating system | Microsoft Windows 10.0.26300 (Windows 11) |
| CPU | 12th Gen Intel(R) Core(TM) i7-1255U |
| Logical cores | 12 |
| .NET SDK | 10.0.402 for the baseline, 10.0.403 for the later sets (selected by both samples' `global.json`, which asks for 10.0.100 with `latestFeature`; 10.0.402 was no longer installed) |

### Baseline: 0.2.0

Package `FrameworkDesignGuidelines.Analyzers` `0.2.0-perfbase.1` and `dotnet-fdg`, both built from commit `2e42877`,
which is the 0.2.0 release commit plus documents.

| Case | Sample | Warm-up, not counted (s) | Runs (s) | Median |
| --- | --- | --- | --- | --- |
| Warm, one project | serilog | 17.03 | 17.21, 18.47, 17.56, 17.04, 16.84 | **17.21 s** |
| Cold, one project | serilog | 20.61 | 20.17, 21.09, 20.97 | **20.97 s** |
| Warm, two projects | polly | 24.24 | 23.32, 21.79, 20.33 | **21.79 s** |

The polly times were still falling from round to round (24.24 s warm-up, then 23.32, 21.79 and 20.33 s), so compare
its median rather than single runs. Other interactive sessions were open on the machine during these runs; the CPU
load was about 1% before the first set and 11% after the last.

The sample copies build a single target framework (see Method), so the cold-start target of under 30 s is judged for a
single-framework build; a project with many target frameworks takes longer.

The commands, in PowerShell from the repository root:

```powershell
$w = "$env:TEMP\fdg-perf"; $b = "$w\perf\baseline"
dotnet pack src/FrameworkDesignGuidelines.Package -c Release -p:Version=0.2.0-perfbase.1 -o $b
dotnet publish src/Fdg.Cli -c Release -o $b\cli
$p = "$b\FrameworkDesignGuidelines.Analyzers.0.2.0-perfbase.1.nupkg"; $c = "$b\cli\dotnet-fdg.dll"
./eng/perf/Measure-Stop.ps1 -Sample serilog -Package $p -Cli $c -WorkRoot $w -Out $w\perf\baseline-serilog-warm.json -Runs 5
./eng/perf/Measure-Stop.ps1 -Sample serilog -Package $p -Cli $c -WorkRoot $w -Out $w\perf\baseline-serilog-cold.json -Runs 3 -Cold
./eng/perf/Measure-Stop.ps1 -Sample polly -Package $p -Cli $c -WorkRoot $w -Out $w\perf\baseline-polly-p2-warm.json -Projects 2 -Runs 3
```

To measure another build, pack it with a version of its own and pass the new package and CLI to the same commands.

### After the Stop changes

Package `FrameworkDesignGuidelines.Analyzers` `0.3.0-perf.1` and `dotnet-fdg`, both built from commit `2b7f1ad`. What
the Stop does differently:

- **One format pass.** Each project's changed files are formatted once instead of until they stop changing (at most
  three runs, the last one usually a run that only verifies). A second run, with the passes left, follows only when the
  build's SARIF log still has a warning of a diagnostic that formatting fixes (`fixDiagnostics` in `rules.json`) in one
  of the project's changed files; if that run changes the files, the project is built again and that build is reported.
  What formatting cannot fix is reported from the build as before. With `FDG_HOOK_BUILD=0` there is no build to tell,
  so the Stop still formats until the files stop changing.
- **No dependencies when none changed.** A project is built with `--no-dependencies` when its referenced projects,
  read statically from the `ProjectReference` items of the project and of the projects it references, are known and no
  file the turn changed or deleted is under one of their directories. An `Include` with an MSBuild property other than
  the project's own directory, an unreadable project file, or a `ProjectReference` in the nearest
  `Directory.Build.props` or `Directory.Build.targets` keeps the dependencies. A build without dependencies that fails
  (other than in copying its output) or writes no log is done once more with them.

In the measured rounds, the build after the one pass finds no fixable diagnostic left in the changed files, so each
project is formatted once and built once. Serilog and Polly.Core reference no project and are built without
dependencies; Polly.Extensions references Polly.Core, which the round changed, so it is built with them. The Stop
output is the same as with 0.2.0.

Between the baseline and these sets, files of the scratch directory under `%TEMP%` were removed: the published 0.2.0
CLI had lost its dependency assemblies and the extracted package cache was incomplete. For the re-measure, the CLI was
published again from commit `2e42877` (with SDK 10.0.403), the original package file was used and the package cache
was refilled.

Warm, one project (serilog), in the order the sets ran on 2026-10-31; after the changes and 0.2.0 alternate:

| Set | Build | Warm-up, not counted (s) | Runs (s) | Median | Spread |
| --- | --- | --- | --- | --- | --- |
| 1 | after the changes | 11.75 | 11.72, 11.98, 11.37, 13.14, 11.44 | 11.72 s | 1.77 s |
| 2 | 0.2.0 | 16.83 | 16.75, 16.80, 16.64, 17.26, 20.94 | 16.80 s | 4.30 s |
| 3 | after the changes | 11.76 | 12.48, 11.78, 12.26, 13.18, 12.00 | 12.26 s | 1.40 s |
| 4 | 0.2.0 | 25.89 | 20.03, 20.76, 18.65, 17.63, 18.26 | 18.65 s | 3.13 s |
| 5 | after the changes | 13.43 | 13.86, 12.25, 12.29, 12.21, 12.19 | 12.25 s | 1.67 s |
| 6 | 0.2.0 | 19.45 | 18.65, 19.50, 22.00, 20.45, 17.96 | 19.50 s | 4.04 s |
| 7 | after the changes | 12.93 | 15.18, 13.33, 12.22, 11.95, 11.74 | 12.22 s | 3.44 s |

All three cases:

| Case | Sample | After the changes: median (runs) | 0.2.0 baseline (2026-10-28) | Change | 0.2.0 re-measured in the same window | Change |
| --- | --- | --- | --- | --- | --- | --- |
| Warm, one project | serilog | **12.22 s** (20 runs, 11.37–15.18) | 17.21 s | −29.0% | 18.65 s (15 runs, 16.64–22.00) | −34.5% |
| Cold, one project | serilog | **17.53 s** (17.53, 18.15, 16.68; warm-up 15.32) | 20.97 s | −16.4% | | |
| Warm, two projects | polly | **15.43 s** (18.72, 15.43, 14.76; warm-up 19.98) | 21.79 s | −29.2% | 21.56 s (20.96, 21.56, 22.87; warm-up 22.04) | −28.4% |
| Warm, one project of polly | polly | 10.89 s (10.88, 12.33, 10.89; warm-up 12.01) | | | | |

The warm medians pool the runs of all sets of a build. Within each pair of neighbouring sets the change is −27.0%
(sets 2 and 3), −34.3% (sets 4 and 5) and −37.3% (sets 6 and 7). 0.2.0 varied more from set to set (medians 16.80 to
19.50 s) than the changed Stop (11.72 to 12.26 s), and ran slower than on 2026-10-28.

**Targets.**

- *Warm, one file: at least 30% below the 0.2.0 baseline.* Met against 0.2.0 measured in the same window, on the same
  sample copy and machine: −34.5% on the pooled medians, and −27.0% to −37.3% pair by pair. Against the 2026-10-28
  figure, the pooled median is 29.0% lower, 0.17 s short of 30%; that figure was taken with SDK 10.0.402 and before the
  machine got slower, which the same-window re-measure shows.
- *Cold: under 30 s.* Met: 17.53 s.

Where the remaining time goes, measured by hand in the serilog copy with the same arguments as the hook: the one
`dotnet format` run takes about 10–11 s (loading the workspace 2.3 s, code style analysis and fixes 3.7 s, analyzer
analysis and fixes 4.3 s, as its diagnostic output reports), the build about 2 s, and the rest of the Stop (git, diffs,
reading the log) well under a second. The verification run that the change removes took about 7 s. Going below that
needs the format run itself to be faster, for example by keeping a workspace loaded between Stops in a resident
process, which is not part of this release.

**More than one project.** The second polly project adds 4.5 s (10.89 to 15.43 s, +42%), far from doubling the time:
the formats of both projects run at once, and only the builds run one after another. One MSBuild call for several
projects, for example through a temporary traversal project, could save at most part of the second build, about 2 s
here, while every project would have to be built with its dependencies and the logs told apart again. The builds stay
one project at a time.

The commands, in PowerShell from the repository root, after the baseline commands above:

```powershell
$w = "$env:TEMP\fdg-perf"; $o = "$w\perf\opt"
dotnet pack src/FrameworkDesignGuidelines.Package -c Release -p:Version=0.3.0-perf.1 -o $o
dotnet publish src/Fdg.Cli -c Release -o $o\cli
$p = "$o\FrameworkDesignGuidelines.Analyzers.0.3.0-perf.1.nupkg"; $c = "$o\cli\dotnet-fdg.dll"
./eng/perf/Measure-Stop.ps1 -Sample serilog -Package $p -Cli $c -WorkRoot $w -Out $w\perf\opt-serilog-warm.json -Runs 5
./eng/perf/Measure-Stop.ps1 -Sample serilog -Package $p -Cli $c -WorkRoot $w -Out $w\perf\opt-serilog-cold.json -Runs 3 -Cold
./eng/perf/Measure-Stop.ps1 -Sample polly -Package $p -Cli $c -WorkRoot $w -Out $w\perf\opt-polly-p2-warm.json -Projects 2 -Runs 3
./eng/perf/Measure-Stop.ps1 -Sample polly -Package $p -Cli $c -WorkRoot $w -Out $w\perf\opt-polly-p1-warm.json -Projects 1 -Runs 3
```

The 0.2.0 sets of the same window ran the baseline commands with the republished CLI, alternating with the warm
serilog sets above.

## Build time with and without the package

How much longer a build of a sample's project takes with the package, all of its rules on, than without it. Measured on
2026-10-31 on the machine above (SDK 10.0.403) with package `0.3.0-perf.1`, by `eng/noise/Measure-Noise.ps1 -Timing`
on the five samples of [noise-samples.md](noise-samples.md):

- each build is a clean build of the sample's project for its framework, with the projects it references: the build
  output is deleted, the project is restored (not timed), and `dotnet build --no-restore` is timed from process start
  to exit;
- after one untimed warm-up build, three builds without the package and three with it run in turn, so the build
  servers are warm, as they are in a Stop; the table gives the medians;
- without the package, a reference to it that a sample has is dropped (`eng/noise/NoPackage.targets`); with it, the
  package is injected as for counting, its SARIF log included;
- all output goes to a directory under the script's work root (`UseArtifactsOutput`, `ArtifactsPath` and
  `OutputPath`), checked by evaluating every project first, so no file of a sample is deleted; for the internal samples
  the script also compared the list of the sample's files before and after the timing builds, and none had appeared
  or changed.

| Sample | Framework | Without the package | With the package | Difference |
| --- | --- | --- | --- | --- |
| serilog | netstandard2.0 | 1.10 s (1.09, 1.13, 1.10) | 1.49 s (1.45, 1.49, 1.54) | +0.39 s (+35%) |
| polly | netstandard2.0 | 2.02 s (1.81, 2.12, 2.02) | 2.43 s (2.42, 2.43, 2.70) | +0.41 s (+20%) |
| newtonsoft-json | netstandard2.0 | 1.61 s (1.63, 1.61, 1.60) | 2.82 s (2.89, 2.82, 2.72) | +1.21 s (+75%) |
| internal-a | net472 | 1.01 s (0.98, 1.01, 1.15) | 1.15 s (1.15, 1.17, 1.14) | +0.14 s (+14%) |
| internal-b | net472 | 7.41 s (7.39, 7.41, 7.96) | 11.02 s (11.39, 10.70, 11.02) | +3.61 s (+49%) |

The package's analyzers run inside the compile step, so the difference grows with the amount of code compiled and the
number of diagnostics reported: the counting build of the same run found 2675 results in newtonsoft-json and 4019 in
internal-b, against 1084 in serilog, 219 in polly and 104 in internal-a. In absolute terms the
difference stays a few seconds; in a Stop it is part of the build's share (about 2 s of the 12 s for serilog), while
the format run takes most of the time.

The command, in PowerShell from the repository root (the internal samples are in a local sample file that is not
committed):

```powershell
./eng/noise/Measure-Noise.ps1 -Samples eng/noise/samples.json,<local samples file> -Package <nupkg> `
    -WorkRoot $env:TEMP/fdg-noise -Out $env:TEMP/fdg-noise/timing.json -Timing
```
