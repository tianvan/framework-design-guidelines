# Performance

How long the plugin's hooks take on real code.

## Stop time

The Stop hook (`dotnet-fdg hook turn-stop`) checks the files the AI changed in a turn: it formats them, builds their
projects and reports the diagnostics on the changed lines. The numbers below are the **baseline of 0.2.0**, measured
before any change to the hook.

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
| .NET SDK | 10.0.402 (selected by both samples' `global.json`) |

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
