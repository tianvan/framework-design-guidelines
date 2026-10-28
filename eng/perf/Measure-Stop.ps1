#Requires -Version 7.2

<#
.SYNOPSIS
Measures how long the Stop hook (dotnet-fdg hook turn-stop) takes for a one-method change in an open-source sample.

.DESCRIPTION
Fetches the sample named -Sample in eng/noise/samples.json at its pinned commit into WorkRoot/perf/<sample>, as
Measure-Noise.ps1 does, resets that copy to the pinned commit (git clean -fdx included), onboards the projects of the
sample's change set below and commits the onboarding in the copy. Onboarding a project writes into its project
file:
- a PackageReference to the -Package version with PrivateAssets="all" (with central package management, the version
  goes into the nearest Directory.Packages.props instead);
- FrameworkDesignGuidelinesProfile set to the sample's profile;
- TargetFrameworks set to the sample's framework, as Measure-Noise.ps1 builds only that framework;
- TreatWarningsAsErrors set to false, as in Measure-Noise.ps1, so that the package's warnings on existing code do not
  fail the build.
The root NuGet.config gets a package source for the directory that holds -Package (and a source mapping for the
package when the file maps sources); a new NuGet.config is created when there is none. NUGET_PACKAGES is
WorkRoot/perf/packages/<package file name without extension>. NuGet keeps an extracted package by id and version
only, so before the first build a cached copy of this id and version whose SHA-512 differs from -Package is deleted.

After one full build of every onboarded project, each round:
1. runs `hook turn-start` with tests/fixtures/hooks/claude/user-prompt-submit.json, its cwd and session_id replaced;
2. adds a method with a one-line try { ... } catch (Exception ex) { ... } to one file in each of the first -Projects
   projects of the change set;
3. times `hook turn-stop` with tests/fixtures/hooks/claude/stop.json, from process start to exit;
4. restores the copy with `git checkout -- .` and `git clean -fd`.
The first round is a warm-up and is not counted. With -Cold, `dotnet build-server shutdown` runs before every Stop;
it stops the build servers of the whole machine, not only those of the sample.
FDG_HOOK_TIMEOUT_SECONDS is 600 so that a slow Stop is measured instead of cut off; FDG_HOOK_BUILD and
FDG_HOOK_MAX_BLOCKS are removed so that their defaults apply. A round fails when the Stop exits with an error, reports
a hook error, a timeout, a skipped check, a failed tool or an unchecked project, leaves a changed file as written
(not reformatted) or writes fewer SARIF logs than the projects it changed.

Writes -Out as { sample, package, cli, projects, cold, runs: [seconds...], median, machine }; machine holds the
operating system, the CPU model, the logical core count and the .NET SDK version the sample builds with. The time
and the hook output of every round, warm-up included, go to WorkRoot/perf/logs.

.PARAMETER Sample
The name of a sample in eng/noise/samples.json that has a change set in this script: serilog (one project) or polly
(up to three projects).

.PARAMETER Package
The FrameworkDesignGuidelines.Analyzers .nupkg to measure.

.PARAMETER Cli
The dotnet-fdg executable, or dotnet-fdg.dll, which is run with dotnet.

.PARAMETER WorkRoot
A scratch directory outside this repository.

.PARAMETER Out
The result JSON file, outside this repository.

.PARAMETER Runs
How many timed rounds follow the warm-up.

.PARAMETER Projects
How many projects each round changes, one file each: 1 to 3, at most the size of the sample's change set.

.PARAMETER Cold
Run `dotnet build-server shutdown` before every Stop. It stops the MSBuild nodes and compiler servers of the
whole machine, so builds running elsewhere start cold too.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Sample,

    [Parameter(Mandatory)]
    [string] $Package,

    [Parameter(Mandatory)]
    [string] $Cli,

    [Parameter(Mandatory)]
    [string] $WorkRoot,

    [Parameter(Mandatory)]
    [string] $Out,

    [ValidateRange(1, 100)]
    [int] $Runs = 5,

    [ValidateRange(1, 3)]
    [int] $Projects = 1,

    [switch] $Cold
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Per sample, the projects a round changes, in order, with the file changed in each; -Projects N changes the first N.
# The first project is the sample's own; the others reference it.
$ChangeSets = @{
    serilog = @(
        @{ Project = 'src/Serilog/Serilog.csproj'; File = 'src/Serilog/Capturing/PropertyBinder.cs' }
    )
    polly = @(
        @{ Project = 'src/Polly.Core/Polly.Core.csproj'; File = 'src/Polly.Core/Timeout/TimeoutResilienceStrategy.cs' }
        @{ Project = 'src/Polly.Extensions/Polly.Extensions.csproj'; File = 'src/Polly.Extensions/Telemetry/TelemetryUtil.cs' }
        @{ Project = 'src/Polly.RateLimiting/Polly.RateLimiting.csproj'; File = 'src/Polly.RateLimiting/RateLimiterResilienceStrategy.cs' }
    )
}

# Added before the last closing brace of each changed file, which closes its only class (file-scoped namespace).
$ProbeMethod = @(
    ''
    '    public static string Probe(int attempt, string key)'
    '    {'
    '        try { return key.Trim(); } catch (Exception ex) { return ex.Message; }'
    '    }'
)

$PackageId = 'FrameworkDesignGuidelines.Analyzers'
$PackageSourceKey = 'fdg-perf'

# Phrases of the hook messages that mean the Stop did not check the turn in full.
$FailurePattern = 'FDG hook error|timed out|was skipped|not checked|not restored|failed for|cannot evaluate|cannot read|newer than this dotnet-fdg'

function Get-FullPath([string] $Path)
{
    return [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path))
}

function Test-IsUnder([string] $Path, [string] $Root)
{
    $relative = [System.IO.Path]::GetRelativePath($Root, $Path)
    return $relative -eq '.' -or -not ($relative.StartsWith('..') -or [System.IO.Path]::IsPathRooted($relative))
}

# Runs an external tool with its arguments passed as an array; returns the exit code and the combined output lines.
function Invoke-Tool([string] $FilePath, [string[]] $ArgumentList)
{
    $output = & $FilePath @ArgumentList 2>&1 | ForEach-Object { "$_" }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = @($output) }
}

function Get-FirstError([string[]] $Lines, [string] $Fallback)
{
    $line = $Lines | Where-Object { $_ -match '(:|^)\s*error\s+[A-Za-z]*\d*\s*:' } | Select-Object -First 1
    if (-not $line)
    {
        $line = $Lines | Where-Object { $_ -match '\b(error|fatal)\b' } | Select-Object -First 1
    }

    if (-not $line)
    {
        return $Fallback
    }

    return $line.Trim()
}

# Runs git in the sample copy and throws when it fails.
function Invoke-Git([string] $Directory, [string[]] $ArgumentList)
{
    $result = Invoke-Tool 'git' (@('-C', $Directory) + $ArgumentList)
    if ($result.ExitCode -ne 0)
    {
        throw "git $($ArgumentList -join ' ') failed with exit code $($result.ExitCode): $(Get-FirstError $result.Output '')"
    }

    return $result.Output
}

# IDE0055 also checks line endings: against end_of_line when the sample's root .editorconfig sets it for C# files, and
# against the platform's newline otherwise. Returns the git settings that check the sample out with those line
# endings, as Measure-Noise.ps1 does; here they are stored in the copy, since every round checks files out again.
function Get-LineEndingSettings([string] $Directory, [string] $Commit)
{
    $endOfLine = $null
    $editorConfig = Invoke-Tool 'git' @('-C', $Directory, 'show', "$($Commit):.editorconfig")
    if ($editorConfig.ExitCode -eq 0)
    {
        $appliesToCSharp = $false
        foreach ($line in $editorConfig.Output)
        {
            if ($line -match '^\s*\[(.*)\]\s*$')
            {
                $appliesToCSharp = $Matches[1] -eq '*' -or $Matches[1] -match '(^|[.,{])cs([,}]|$)'
            }
            elseif ($appliesToCSharp -and $line -match '^\s*end_of_line\s*=\s*(lf|crlf)\s*$')
            {
                $endOfLine = $Matches[1]
            }
        }
    }

    if (-not $endOfLine)
    {
        $endOfLine = if ($IsWindows) { 'crlf' } else { 'lf' }
    }

    if ($endOfLine -eq 'crlf')
    {
        return [ordered]@{ 'core.autocrlf' = 'true' }
    }

    return [ordered]@{ 'core.autocrlf' = 'false'; 'core.eol' = 'lf' }
}

# Fetches the sample at its pinned commit into $Directory, or reuses an earlier copy; either way the copy ends up at
# the pinned commit on branch perf with no local change and no untracked or ignored file.
function Initialize-SampleCopy([object] $SampleEntry, [string] $Directory)
{
    $commit = [string]$SampleEntry.commit
    if (-not (Test-Path -LiteralPath (Join-Path $Directory '.git')))
    {
        New-Item -ItemType Directory -Path $Directory -Force | Out-Null
        Invoke-Git $Directory @('init', '--quiet') | Out-Null
    }

    if ((Invoke-Tool 'git' @('-C', $Directory, 'cat-file', '-e', "$commit^{commit}")).ExitCode -ne 0)
    {
        Invoke-Git $Directory @('fetch', '--quiet', '--depth', '1', $SampleEntry.repository, $commit) | Out-Null
    }

    $settings = Get-LineEndingSettings $Directory $commit
    foreach ($name in $settings.Keys)
    {
        Invoke-Git $Directory @('config', $name, $settings[$name]) | Out-Null
    }

    Invoke-Git $Directory @('checkout', '--quiet', '--force', '-B', 'perf', $commit) | Out-Null
    Invoke-Git $Directory @('clean', '-fdxq') | Out-Null
}

# Reads a text file with its encoding and line break, so that an edit keeps both.
function Read-TextFile([string] $Path)
{
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.UTF8Encoding]::new($false).GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    return [pscustomobject]@{ Text = $text; HasBom = $hasBom; Newline = $newline }
}

function Write-TextFile([string] $Path, [string] $Text, [bool] $HasBom)
{
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($HasBom))
}

# Inserts lines just before the closing </Project> of an MSBuild file.
function Add-ToProjectFile([string] $Path, [string[]] $Lines)
{
    $file = Read-TextFile $Path
    $index = $file.Text.LastIndexOf('</Project>', [System.StringComparison]::Ordinal)
    if ($index -lt 0)
    {
        throw "$Path has no </Project>."
    }

    Write-TextFile $Path ($file.Text.Insert($index, ($Lines -join $file.Newline) + $file.Newline)) $file.HasBom
}

# Evaluates MSBuild properties of a project without building it.
function Get-ProjectProperties([string] $ProjectPath, [string[]] $Names)
{
    $result = Invoke-Tool 'dotnet' (@('msbuild', $ProjectPath, '-nologo') + @($Names | ForEach-Object { "-getProperty:$_" }))
    if ($result.ExitCode -ne 0)
    {
        throw "Evaluating $ProjectPath failed: $(Get-FirstError $result.Output "exit code $($result.ExitCode)")"
    }

    $text = $result.Output -join "`n"
    return $text.Substring([Math]::Max(0, $text.IndexOf('{'))) | ConvertFrom-Json | Select-Object -ExpandProperty Properties
}

# The nearest file named $Name at or above $Directory, up to $Root; $null when there is none.
function Find-FileAbove([string] $Directory, [string] $Name, [string] $Root)
{
    $current = [System.IO.DirectoryInfo]::new($Directory)
    while ($null -ne $current -and (Test-IsUnder $current.FullName $Root))
    {
        $candidate = Get-ChildItem -LiteralPath $current.FullName -File -Force | Where-Object { $_.Name -ieq $Name } | Select-Object -First 1
        if ($candidate)
        {
            return $candidate.FullName
        }

        $current = $current.Parent
    }

    return $null
}

# Onboards one project of the copy: package reference, profile, framework and warnings as errors (see DESCRIPTION).
function Add-PackageToProject([string] $Root, [string] $ProjectPath, [object] $SampleEntry, [string] $Version)
{
    $properties = Get-ProjectProperties $ProjectPath @('ManagePackageVersionsCentrally', 'TargetFrameworks')
    $central = $properties.ManagePackageVersionsCentrally -eq 'true'
    $propertyLines = @(
        '  <PropertyGroup>'
        "    <FrameworkDesignGuidelinesProfile>$($SampleEntry.profile)</FrameworkDesignGuidelinesProfile>"
    )
    if ($properties.TargetFrameworks)
    {
        $propertyLines += "    <TargetFrameworks>$($SampleEntry.framework)</TargetFrameworks>"
    }

    $propertyLines += @(
        '    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>'
        '  </PropertyGroup>'
    )
    $versionAttribute = if ($central) { '' } else { " Version=`"$Version`"" }
    Add-ToProjectFile $ProjectPath ($propertyLines + @(
            '  <ItemGroup>'
            "    <PackageReference Include=`"$PackageId`"$versionAttribute PrivateAssets=`"all`" />"
            '  </ItemGroup>'
        ))

    if ($central)
    {
        $packagesProps = Find-FileAbove ([System.IO.Path]::GetDirectoryName($ProjectPath)) 'Directory.Packages.props' $Root
        if (-not $packagesProps)
        {
            throw "$ProjectPath uses central package management but has no Directory.Packages.props in the sample."
        }

        if (-not (Select-String -LiteralPath $packagesProps -Pattern "Include=`"$([regex]::Escape($PackageId))`"" -Quiet))
        {
            Add-ToProjectFile $packagesProps @(
                '  <ItemGroup>'
                "    <PackageVersion Include=`"$PackageId`" Version=`"$Version`" />"
                '  </ItemGroup>'
            )
        }
    }
}

# Adds the directory that holds the package as a source to the root NuGet.config of the copy.
function Add-PackageSource([string] $Root, [string] $SourceDirectory)
{
    $value = [System.Security.SecurityElement]::Escape($SourceDirectory)
    $configPath = Get-ChildItem -LiteralPath $Root -File -Force | Where-Object { $_.Name -ieq 'NuGet.config' } | Select-Object -First 1 -ExpandProperty FullName
    if (-not $configPath)
    {
        $lines = @(
            '<?xml version="1.0" encoding="utf-8"?>'
            '<configuration>'
            '  <packageSources>'
            "    <add key=`"$PackageSourceKey`" value=`"$value`" />"
            '  </packageSources>'
            '</configuration>'
        )
        Write-TextFile (Join-Path $Root 'NuGet.config') (($lines -join [Environment]::NewLine) + [Environment]::NewLine) $false
        return
    }

    $file = Read-TextFile $configPath
    $text = $file.Text
    $index = $text.IndexOf('</packageSources>', [System.StringComparison]::Ordinal)
    if ($index -lt 0)
    {
        throw "$configPath has no </packageSources>."
    }

    $text = $text.Insert($index, "  <add key=`"$PackageSourceKey`" value=`"$value`" />$($file.Newline)  ")
    $index = $text.IndexOf('</packageSourceMapping>', [System.StringComparison]::Ordinal)
    if ($index -ge 0)
    {
        $text = $text.Insert($index, "  <packageSource key=`"$PackageSourceKey`">$($file.Newline)      <package pattern=`"$PackageId`" />$($file.Newline)    </packageSource>$($file.Newline)  ")
    }

    Write-TextFile $configPath $text $file.HasBom
}

# Adds $ProbeMethod before the last closing brace of the file; returns the text written.
function Add-ProbeMethod([string] $Path)
{
    $file = Read-TextFile $Path
    $lines = [System.Collections.Generic.List[string]]::new([string[]]($file.Text -split '\r?\n'))
    $last = $lines.FindLastIndex([Predicate[string]] { param($line) $line.Trim() -eq '}' })
    if ($last -lt 0)
    {
        throw "$Path has no closing brace on a line of its own."
    }

    $lines.InsertRange($last, [string[]]$ProbeMethod)
    $text = $lines -join $file.Newline
    Write-TextFile $Path $text $file.HasBom
    return $text
}

function Get-Median([double[]] $Values)
{
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1)
    {
        return $sorted[$middle]
    }

    return [Math]::Round(($sorted[$middle - 1] + $sorted[$middle]) / 2, 2)
}

function Get-CpuName
{
    if ($IsWindows)
    {
        return (Get-CimInstance -ClassName Win32_Processor | Select-Object -First 1).Name.Trim()
    }

    if ($IsLinux)
    {
        $match = Select-String -LiteralPath '/proc/cpuinfo' -Pattern '^model name\s*:\s*(.+)$' | Select-Object -First 1
        if ($match)
        {
            return $match.Matches[0].Groups[1].Value.Trim()
        }
    }

    if ($IsMacOS)
    {
        return (& sysctl -n machdep.cpu.brand_string).Trim()
    }

    return 'unknown'
}

# Runs one hook event of the CLI with the payload on stdin; returns the exit code, the output and the seconds from
# process start to exit.
function Invoke-Hook([string] $HookEvent, [string] $Payload, [string] $WorkingDirectory)
{
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($cliFile)
    foreach ($argument in @($cliArguments) + @('hook', $HookEvent))
    {
        $startInfo.ArgumentList.Add($argument)
    }

    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardInputEncoding = $utf8
    $startInfo.StandardOutputEncoding = $utf8
    $startInfo.StandardErrorEncoding = $utf8

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try
    {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($Payload)
        $process.StandardInput.Close()
        if (-not $process.WaitForExit([int][TimeSpan]::FromMinutes(15).TotalMilliseconds))
        {
            $process.Kill($true)
            throw "hook $HookEvent did not exit within 15 minutes."
        }

        $process.WaitForExit()
        $stopwatch.Stop()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $stdout.GetAwaiter().GetResult()
            Error = $stderr.GetAwaiter().GetResult()
            Seconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        }
    }
    finally
    {
        $process.Dispose()
    }
}

function New-Payload([string] $FixtureName, [string] $Cwd, [string] $SessionId)
{
    $payload = Get-Content -LiteralPath (Join-Path $fixtureDirectory $FixtureName) -Raw | ConvertFrom-Json
    $payload.cwd = $Cwd
    $payload.session_id = $SessionId
    return $payload | ConvertTo-Json -Depth 16 -Compress
}

# One round: turn-start, change, timed turn-stop, checks, restore. Returns the Stop's seconds.
function Invoke-Round([string] $Label)
{
    $start = Invoke-Hook 'turn-start' $turnStartPayload $sampleRoot
    if ($start.ExitCode -ne 0)
    {
        throw "hook turn-start exited with $($start.ExitCode): $($start.Error.Trim())"
    }

    $written = @{}
    foreach ($entry in $changed)
    {
        $path = Join-Path $sampleRoot $entry.File
        $written[$path] = Add-ProbeMethod $path
    }

    if ($Cold)
    {
        $shutdown = Invoke-Tool 'dotnet' @('build-server', 'shutdown')
        if ($shutdown.ExitCode -ne 0)
        {
            throw "dotnet build-server shutdown failed: $(Get-FirstError $shutdown.Output "exit code $($shutdown.ExitCode)")"
        }
    }

    $startedUtc = [DateTime]::UtcNow
    $stop = Invoke-Hook 'turn-stop' $turnStopPayload $sampleRoot
    Add-Content -LiteralPath $logPath -Value @(
        "== $Label`: $($stop.Seconds) s, exit code $($stop.ExitCode)"
        "stdout: $($stop.Output.Trim())"
        "stderr: $($stop.Error.Trim())"
    )

    $problems = [System.Collections.Generic.List[string]]::new()
    if ($stop.ExitCode -ne 0)
    {
        $problems.Add("hook turn-stop exited with $($stop.ExitCode): $($stop.Error.Trim())")
    }

    if ($stop.Output -match $FailurePattern)
    {
        $problems.Add("the Stop did not check the turn in full: $($stop.Output.Trim())")
    }

    foreach ($path in $written.Keys)
    {
        if ((Read-TextFile $path).Text -ceq $written[$path])
        {
            $problems.Add("the Stop did not reformat $path.")
        }
    }

    $stateDirectory = Join-Path $sampleRoot '.git' 'fdg' $sessionId
    $logs = @(Get-ChildItem -LiteralPath $stateDirectory -Recurse -File -Filter '*.sarif' -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -ge $startedUtc })
    if ($logs.Count -lt $changed.Count)
    {
        $problems.Add("the Stop wrote $($logs.Count) SARIF log(s) for $($changed.Count) changed project(s).")
    }

    Invoke-Git $sampleRoot @('checkout', '--quiet', '--', '.') | Out-Null
    Invoke-Git $sampleRoot @('clean', '-fdq') | Out-Null
    if ($problems.Count -gt 0)
    {
        throw "$Label`: $($problems -join ' ') See $logPath."
    }

    Write-Host "$Label`: $($stop.Seconds) s"
    return $stop.Seconds
}

$repositoryRoot = Get-FullPath (Join-Path $PSScriptRoot '..' '..')
$fixtureDirectory = Join-Path $repositoryRoot 'tests' 'fixtures' 'hooks' 'claude'
$packagePath = Get-FullPath $Package
$cliPath = Get-FullPath $Cli
$workRootPath = Get-FullPath $WorkRoot
$outPath = Get-FullPath $Out

foreach ($outputPath in @($workRootPath, $outPath))
{
    if (Test-IsUnder $outputPath $repositoryRoot)
    {
        throw "'$outputPath' is inside '$repositoryRoot'; WorkRoot and Out must be outside the repository."
    }
}

if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf) -or [System.IO.Path]::GetExtension($packagePath) -ne '.nupkg')
{
    throw "Package '$packagePath' is not a .nupkg file."
}

if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf))
{
    throw "Cli '$cliPath' does not exist."
}

if ([System.IO.Path]::GetExtension($cliPath) -eq '.dll')
{
    $cliFile = 'dotnet'
    $cliArguments = @($cliPath)
}
else
{
    $cliFile = $cliPath
    $cliArguments = @()
}

$sampleEntry = @(Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng' 'noise' 'samples.json') -Raw | ConvertFrom-Json) |
    Where-Object { $_.name -eq $Sample } | Select-Object -First 1
if (-not $sampleEntry)
{
    throw "Sample '$Sample' is not in eng/noise/samples.json."
}

if (-not $ChangeSets.ContainsKey($Sample))
{
    throw "Sample '$Sample' has no change set in this script; use one of: $($ChangeSets.Keys -join ', ')."
}

$changeSet = @($ChangeSets[$Sample])
if ($Projects -gt $changeSet.Count)
{
    throw "Sample '$Sample' has $($changeSet.Count) project(s) in its change set; -Projects $Projects is too many."
}

$changed = @($changeSet | Select-Object -First $Projects)

# The package id and version, from the package's own manifest.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try
{
    $nuspecEntry = $archive.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.Name -like '*.nuspec' } | Select-Object -First 1
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try
    {
        [xml]$nuspec = $reader.ReadToEnd()
    }
    finally
    {
        $reader.Dispose()
    }
}
finally
{
    $archive.Dispose()
}

$packageVersion = $nuspec.package.metadata.version
if ($nuspec.package.metadata.id -ne $PackageId)
{
    throw "Package '$packagePath' is $($nuspec.package.metadata.id), not $PackageId."
}

$perfRoot = Join-Path $workRootPath 'perf'
$sampleRoot = Join-Path $perfRoot $Sample
$logsDirectory = Join-Path $perfRoot 'logs'
New-Item -ItemType Directory -Path $logsDirectory -Force | Out-Null
$runName = "$Sample-p$Projects$(if ($Cold) { '-cold' })-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$logPath = Join-Path $logsDirectory "$runName.log"

$savedEnvironment = @{}
foreach ($name in 'NUGET_PACKAGES', 'FDG_HOOK_TIMEOUT_SECONDS', 'FDG_HOOK_BUILD', 'FDG_HOOK_MAX_BLOCKS')
{
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

Push-Location
try
{
    $env:NUGET_PACKAGES = Join-Path $perfRoot 'packages' ([System.IO.Path]::GetFileNameWithoutExtension($packagePath))
    $env:FDG_HOOK_TIMEOUT_SECONDS = '600'
    $env:FDG_HOOK_BUILD = $null
    $env:FDG_HOOK_MAX_BLOCKS = $null

    Write-Host "== $Sample at $($sampleEntry.commit) into $sampleRoot"
    Initialize-SampleCopy $sampleEntry $sampleRoot
    # dotnet resolves the SDK from the sample's global.json.
    Set-Location -LiteralPath $sampleRoot

    foreach ($entry in $changeSet)
    {
        Add-PackageToProject $sampleRoot (Join-Path $sampleRoot $entry.Project) $sampleEntry $packageVersion
    }

    Add-PackageSource $sampleRoot ([System.IO.Path]::GetDirectoryName($packagePath))
    Invoke-Git $sampleRoot @('add', '--all') | Out-Null
    Invoke-Git $sampleRoot @('-c', 'user.name=Measure-Stop', '-c', 'user.email=measure-stop@invalid', 'commit', '--quiet', '-m', "Onboard $PackageId $packageVersion") | Out-Null

    # NuGet reuses a cached package of the same id and version; one from another package file would be measured instead.
    $cachedPackage = Join-Path $env:NUGET_PACKAGES $PackageId.ToLowerInvariant() $packageVersion.ToLowerInvariant()
    if (Test-Path -LiteralPath $cachedPackage)
    {
        $hash = [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData([System.IO.File]::ReadAllBytes($packagePath)))
        $cachedHash = Get-ChildItem -LiteralPath $cachedPackage -File -Filter '*.nupkg.sha512' | Select-Object -First 1 |
            ForEach-Object { (Get-Content -LiteralPath $_.FullName -Raw).Trim() }
        if ($cachedHash -ne $hash)
        {
            Write-Host "== $cachedPackage was extracted from another package file; deleting it"
            Remove-Item -LiteralPath $cachedPackage -Recurse -Force
        }
    }

    Write-Host "== full build"
    foreach ($entry in $changeSet)
    {
        $build = Invoke-Tool 'dotnet' @('build', (Join-Path $sampleRoot $entry.Project))
        Add-Content -LiteralPath (Join-Path $logsDirectory "$runName-build.log") -Value $build.Output
        if ($build.ExitCode -ne 0)
        {
            throw "Building $($entry.Project) failed: $(Get-FirstError $build.Output "exit code $($build.ExitCode)")"
        }
    }

    $sdkVersion = ((Invoke-Tool 'dotnet' @('--version')).Output | Select-Object -First 1).Trim()
    $sessionId = [guid]::NewGuid().ToString()
    $turnStartPayload = New-Payload 'user-prompt-submit.json' $sampleRoot $sessionId
    $turnStopPayload = New-Payload 'stop.json' $sampleRoot $sessionId

    Invoke-Round 'warm-up' | Out-Null
    $seconds = @(for ($run = 1; $run -le $Runs; $run++)
        {
            Invoke-Round "run $run"
        })
}
finally
{
    Pop-Location
    foreach ($name in $savedEnvironment.Keys)
    {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
    }
}

$result = [ordered]@{
    sample = $Sample
    package = $packagePath
    cli = $cliPath
    projects = $Projects
    cold = [bool]$Cold
    runs = $seconds
    median = Get-Median $seconds
    machine = [ordered]@{
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        cpu = Get-CpuName
        logicalCores = [Environment]::ProcessorCount
        dotnetSdk = $sdkVersion
    }
}

New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($outPath)) -Force | Out-Null
Set-Content -LiteralPath $outPath -Value ($result | ConvertTo-Json -Depth 4)
Write-Host "median: $($result.median) s; wrote $outPath"
