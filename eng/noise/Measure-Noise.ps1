<#
.SYNOPSIS
Counts the diagnostics a FrameworkDesignGuidelines.Analyzers package reports on sample projects.

.DESCRIPTION
Extracts the package to WorkRoot/package and builds each sample with Inject.props and Inject.targets passed as
CustomBeforeMicrosoftCommonProps and CustomAfterMicrosoftCommonTargets, so no sample file is edited. Remote samples
are fetched at their pinned commit into WorkRoot/<name>; local samples are built in place without any git operation.

Writes -Out as { "<sample>": { "<ruleId>": <count> }, "overrides": { "<sample>": { ... } } }. A sample whose build
fails is recorded as { "buildFailed": "<first error>" }. Every counted location is written to
WorkRoot/locations/<sample>.tsv (ruleId, relative path, line, message).

.PARAMETER Samples
JSON files, each an array of samples. A remote sample has name, repository, commit, project, framework and profile;
a local sample has path (absolute) instead of repository and commit. project is relative to the sample root.

.PARAMETER Package
The FrameworkDesignGuidelines.Analyzers .nupkg to measure.

.PARAMETER WorkRoot
A scratch directory outside this repository and outside every local sample.

.PARAMETER Out
The result JSON file, outside this repository and outside every local sample.

.PARAMETER Diagnostics
When given, only these diagnostic ids are counted and listed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $Samples,

    [Parameter(Mandatory)]
    [string] $Package,

    [Parameter(Mandatory)]
    [string] $WorkRoot,

    [Parameter(Mandatory)]
    [string] $Out,

    [string[]] $Diagnostics
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullPath([string] $Path)
{
    return [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path))
}

function Test-IsUnder([string] $Path, [string] $Root)
{
    $relative = [System.IO.Path]::GetRelativePath($Root, $Path)
    return $relative -eq '.' -or -not ($relative.StartsWith('..') -or [System.IO.Path]::IsPathRooted($relative))
}

function Get-RelativePath([string] $Root, [string] $Path)
{
    if (Test-IsUnder $Path $Root)
    {
        return [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
    }

    return $Path.Replace('\', '/')
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

function Get-Property([object] $Object, [string] $Name)
{
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name])
    {
        return $Object.$Name
    }

    return $null
}

# IDE0055 also checks line endings: against end_of_line when the sample's root .editorconfig sets it for C# files, and
# against the platform's newline otherwise. Returns git options that check the sample out with those same line
# endings, so that user or platform git settings do not show up as formatting noise.
function Get-LineEndingOptions([string] $Directory)
{
    $endOfLine = $null
    $editorConfig = Invoke-Tool 'git' @('-C', $Directory, 'show', 'FETCH_HEAD:.editorconfig')
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
        return @('-c', 'core.autocrlf=true')
    }

    return @('-c', 'core.autocrlf=false', '-c', 'core.eol=lf')
}

# Fetches a remote sample at its pinned commit into WorkRoot/<name>; returns an error text, or $null on success.
function Initialize-RemoteSample([object] $Sample, [string] $Directory)
{
    if (Test-Path -LiteralPath $Directory)
    {
        return $null
    }

    New-Item -ItemType Directory -Path $Directory | Out-Null
    $steps = @(
        , @('-C', $Directory, 'init', '--quiet')
        , @('-C', $Directory, 'fetch', '--quiet', '--depth', '1', $Sample.repository, $Sample.commit)
        , @('checkout', '--quiet', 'FETCH_HEAD')
    )
    foreach ($step in $steps)
    {
        if ($step[0] -eq 'checkout')
        {
            $step = @('-C', $Directory) + (Get-LineEndingOptions $Directory) + $step
        }

        $result = Invoke-Tool 'git' $step
        if ($result.ExitCode -ne 0)
        {
            # Removed so that the next run fetches again instead of building a partial checkout.
            Remove-Item -LiteralPath $Directory -Recurse -Force
            $command = $step | Where-Object { $_ -in 'init', 'fetch', 'checkout' } | Select-Object -First 1
            return Get-FirstError $result.Output "git $command failed with exit code $($result.ExitCode)"
        }
    }

    return $null
}

# Lists the .editorconfig files that apply to the project and the dotnet_diagnostic.* and dotnet_naming_* entries
# they set: those from the project directory up to the first root = true, and those below the project directory.
function Get-EditorConfigOverrides([string] $SampleRoot, [string] $ProjectDirectory)
{
    $files = [System.Collections.Generic.List[string]]::new()
    $directory = [System.IO.DirectoryInfo]::new($ProjectDirectory)
    while ($null -ne $directory)
    {
        $candidate = Join-Path $directory.FullName '.editorconfig'
        if (Test-Path -LiteralPath $candidate -PathType Leaf)
        {
            $files.Add($candidate)
            if (Select-String -LiteralPath $candidate -Pattern '^\s*root\s*=\s*true\s*$' -Quiet)
            {
                break
            }
        }

        $directory = $directory.Parent
    }

    if (Test-Path -LiteralPath $ProjectDirectory)
    {
        Get-ChildItem -LiteralPath $ProjectDirectory -Recurse -File -Force -Filter '.editorconfig' |
            Where-Object { $_.DirectoryName -ne $ProjectDirectory -and $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' } |
            ForEach-Object { $files.Add($_.FullName) }
    }

    $diagnostic = [System.Collections.Generic.List[string]]::new()
    $naming = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $files)
    {
        foreach ($line in Get-Content -LiteralPath $file)
        {
            $entry = $line.Trim()
            if ($entry -match '^dotnet_diagnostic\.')
            {
                $diagnostic.Add($entry)
            }
            elseif ($entry -match '^dotnet_naming_')
            {
                $naming.Add($entry)
            }
        }
    }

    return [ordered]@{
        editorconfig = @($files | ForEach-Object { Get-RelativePath $SampleRoot $_ })
        dotnetDiagnostic = @($diagnostic)
        dotnetNaming = @($naming)
    }
}

# Reads one SARIF 2.1 log: drops suppressed results and returns one location per (file, line, ruleId).
function Read-SarifLocations([string] $SarifPath, [string] $SampleRoot, [string[]] $Only)
{
    $log = Get-Content -LiteralPath $SarifPath -Raw | ConvertFrom-Json -Depth 64
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    $locations = [System.Collections.Generic.List[object]]::new()
    foreach ($run in @($log.runs))
    {
        foreach ($result in @(Get-Property $run 'results'))
        {
            if ($null -eq $result)
            {
                continue
            }

            if (@(Get-Property $result 'suppressions').Where({ $null -ne $_ }).Count -gt 0)
            {
                continue
            }

            $ruleId = [string]$result.ruleId
            if ($Only -and $Only -notcontains $ruleId)
            {
                continue
            }

            $file = ''
            $line = 0
            $physical = Get-Property (@(Get-Property $result 'locations') | Select-Object -First 1) 'physicalLocation'
            if ($null -ne $physical)
            {
                $uri = [string](Get-Property (Get-Property $physical 'artifactLocation') 'uri')
                if ($uri)
                {
                    $file = Get-RelativePath $SampleRoot ([System.Uri]::new($uri).LocalPath)
                }

                $startLine = Get-Property (Get-Property $physical 'region') 'startLine'
                if ($null -ne $startLine)
                {
                    $line = [int]$startLine
                }
            }

            if (-not $seen.Add("$file`n$line`n$ruleId"))
            {
                continue
            }

            $message = [string](Get-Property (Get-Property $result 'message') 'text')
            $locations.Add([pscustomobject]@{
                    RuleId = $ruleId
                    Path = $file
                    Line = $line
                    Message = ($message -replace '\s+', ' ').Trim()
                })
        }
    }

    return $locations
}

# pwsh -File passes "a,b" as one string, so comma-separated lists are split here.
$Samples = @($Samples -split ',' | ForEach-Object Trim | Where-Object { $_ })
$Diagnostics = @($Diagnostics -split ',' | ForEach-Object Trim | Where-Object { $_ })

$repositoryRoot = Get-FullPath (Join-Path $PSScriptRoot '..' '..')
$injectProps = Get-FullPath (Join-Path $PSScriptRoot 'Inject.props')
$injectTargets = Get-FullPath (Join-Path $PSScriptRoot 'Inject.targets')
$packagePath = Get-FullPath $Package
$workRootPath = Get-FullPath $WorkRoot
$outPath = Get-FullPath $Out

$sampleList = [System.Collections.Generic.List[object]]::new()
foreach ($samplesFile in $Samples)
{
    foreach ($sample in @(Get-Content -LiteralPath (Get-FullPath $samplesFile) -Raw | ConvertFrom-Json))
    {
        if ([string]$sample.name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$')
        {
            throw "Sample name '$($sample.name)' must be letters, digits, '.', '_' or '-'."
        }

        if ($sampleList.Where({ $_.name -eq $sample.name }).Count -gt 0)
        {
            throw "Sample name '$($sample.name)' is listed twice."
        }

        $sampleList.Add($sample)
    }
}

# WorkRoot and every output stay outside this repository and outside every local sample.
$protectedRoots = @($repositoryRoot) + @($sampleList | Where-Object { Get-Property $_ 'path' } | ForEach-Object { Get-FullPath $_.path })
foreach ($protectedRoot in $protectedRoots)
{
    foreach ($outputPath in @($workRootPath, $outPath))
    {
        if (Test-IsUnder $outputPath $protectedRoot)
        {
            throw "'$outputPath' is inside '$protectedRoot'; WorkRoot and Out must be outside the repository and every local sample."
        }
    }
}

$packageDirectory = Join-Path $workRootPath 'package'
if (Test-Path -LiteralPath $packageDirectory)
{
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDirectory | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $packageDirectory)

$locationsDirectory = Join-Path $workRootPath 'locations'
$logsDirectory = Join-Path $workRootPath 'logs'
New-Item -ItemType Directory -Path $locationsDirectory, $logsDirectory -Force | Out-Null

$results = [ordered]@{}
$overrides = [ordered]@{}
foreach ($sample in $sampleList)
{
    $name = [string]$sample.name
    Write-Host "== $name"
    $localPath = Get-Property $sample 'path'
    if ($localPath)
    {
        $sampleRoot = Get-FullPath $localPath
        $fetchError = $null
    }
    else
    {
        $sampleRoot = Join-Path $workRootPath $name
        $fetchError = Initialize-RemoteSample $sample $sampleRoot
    }

    $projectPath = [System.IO.Path]::GetFullPath((Join-Path $sampleRoot $sample.project))
    $overrides[$name] = Get-EditorConfigOverrides $sampleRoot ([System.IO.Path]::GetDirectoryName($projectPath))
    if ($fetchError)
    {
        $results[$name] = [ordered]@{ buildFailed = $fetchError }
        continue
    }

    $sarifDirectory = Join-Path (Join-Path $workRootPath 'sarif') $name
    if (Test-Path -LiteralPath $sarifDirectory)
    {
        Remove-Item -LiteralPath $sarifDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $sarifDirectory | Out-Null
    $buildArguments = @(
        'build', $projectPath, '-f', $sample.framework, '--no-incremental'
        '-p:TreatWarningsAsErrors=false'
        "-p:CustomBeforeMicrosoftCommonProps=$injectProps"
        "-p:CustomAfterMicrosoftCommonTargets=$injectTargets"
        "-p:FdgNoisePackage=$packageDirectory"
        "-p:FdgNoiseSarifDir=$sarifDirectory"
        "-p:FrameworkDesignGuidelinesProfile=$($sample.profile)"
    )
    $build = Invoke-Tool 'dotnet' $buildArguments
    Set-Content -LiteralPath (Join-Path $logsDirectory "$name.log") -Value $build.Output

    # Only the sample's own project is counted; referenced projects write their own logs next to it.
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    $sarifPath = Join-Path $sarifDirectory "$projectName-$($sample.framework).sarif"
    if ($build.ExitCode -ne 0)
    {
        $results[$name] = [ordered]@{ buildFailed = Get-FirstError $build.Output "dotnet build failed with exit code $($build.ExitCode)" }
        continue
    }

    if (-not (Test-Path -LiteralPath $sarifPath))
    {
        $results[$name] = [ordered]@{ buildFailed = "No SARIF log at $sarifPath" }
        continue
    }

    $locations = @(Read-SarifLocations $sarifPath $sampleRoot $Diagnostics | Sort-Object RuleId, Path, Line)
    $tsv = @("ruleId`tpath`tline`tmessage") + @($locations | ForEach-Object { "$($_.RuleId)`t$($_.Path)`t$($_.Line)`t$($_.Message)" })
    Set-Content -LiteralPath (Join-Path $locationsDirectory "$name.tsv") -Value $tsv

    $counts = [ordered]@{}
    foreach ($group in $locations | Group-Object RuleId | Sort-Object Name)
    {
        $counts[$group.Name] = $group.Count
    }

    $results[$name] = $counts
}

$results['overrides'] = $overrides
$outDirectory = [System.IO.Path]::GetDirectoryName($outPath)
New-Item -ItemType Directory -Path $outDirectory -Force | Out-Null
Set-Content -LiteralPath $outPath -Value ($results | ConvertTo-Json -Depth 8)
Write-Host "Wrote $outPath"
