# Runs the FDG version selected by the current Git worktree.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$fdgHost = '@FDG_HOST@'
$mode = if ($args.Count -gt 0) { $args[0] } else { '' }
$event = if ($args.Count -gt 1) { $args[1] } else { '' }
$launcher = ".$fdgHost/hooks/fdg/fdg.ps1"

function Exit-Fdg([int] $status) {
    # Preserve the exit code even when Codex invokes the script through pwsh -Command.
    [Environment]::Exit($status)
}

function Write-Notice([string] $message) {
    [Console]::Out.WriteLine((@{systemMessage = $message} | ConvertTo-Json -Compress))
}

$root = git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0) { Exit-Fdg 0 }
Set-Location -LiteralPath $root
$receipt = ".$fdgHost/hooks/fdg-install.json"
foreach ($path in @(".$fdgHost", ".$fdgHost/hooks", ".$fdgHost/hooks/fdg", $launcher, $receipt)) {
    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if (!$item -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        if ($mode -eq 'hook') { Exit-Fdg 0 }
        [Console]::Error.WriteLine("FDG: no $fdgHost installation record inside the current worktree.")
        Exit-Fdg 1
    }
}

try {
    $version = (Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json).toolVersion
} catch {
    $version = $null
}
if ($version -isnot [string] -or $version.Length -gt 128 -or
    $version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*)?\z') {
    if ($mode -eq 'hook') {
        if ($event -eq 'session-start') { Write-Notice 'FDG: invalid installation record. Re-run FDG init in this worktree.' }
        Exit-Fdg 0
    }
    [Console]::Error.WriteLine("FDG: invalid installation record: $receipt")
    Exit-Fdg 1
}

$cache = $env:FDG_TOOL_CACHE
if (!$cache) {
    if ($IsWindows) {
        $cache = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FDG/tools'
    } else {
        $cacheBase = $env:XDG_CACHE_HOME
        if (!$cacheBase -or ![IO.Path]::IsPathFullyQualified($cacheBase)) {
            $cacheBase = Join-Path $HOME '.cache'
        }
        $cache = Join-Path $cacheBase 'fdg/tools'
    }
}

$nativeArch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$processArch = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$architectures = @($processArch, $nativeArch)
if (($IsWindows -or $IsMacOS) -and $nativeArch -eq 'arm64') { $architectures += 'x64' }
if ($IsWindows -and $nativeArch -eq 'x64') { $architectures += 'x86' }
$platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
$exe = if ($IsWindows) { 'dotnet-fdg.exe' } else { 'dotnet-fdg' }
$tool = $null
if ([IO.Path]::IsPathFullyQualified($cache)) {
    foreach ($arch in $architectures) {
        $candidate = Join-Path $cache "$platform-$arch/$($version.ToLowerInvariant())/$exe"
        if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $relative = [IO.Path]::GetRelativePath($root, $candidate)
        $outside = [IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar)
        $links = Get-Item -LiteralPath $candidate, ([IO.Path]::GetDirectoryName($candidate)) -Force |
            Where-Object Attributes -Match ReparsePoint
        if ($outside -and !$links) {
            $tool = $candidate
            break
        }
    }
}
$ready = $null -ne $tool
$prepare = "pwsh -NoProfile -File $launcher prepare"

if ($mode -eq 'prepare') {
    if ($ready -and ((& $tool --version 2>$null) -ceq $version) -and $LASTEXITCODE -eq 0) {
        [Console]::Out.WriteLine("FDG $version is ready.")
        Exit-Fdg 0
    }
    & dotnet tool exec --yes "dotnet-fdg@$version" -- cache prepare --host $fdgHost
    Exit-Fdg $LASTEXITCODE
}

if (!$ready) {
    if ($mode -eq 'hook') {
        if ($event -eq 'session-start') { Write-Notice "FDG $version is not prepared. From the repository root, run: $prepare" }
        Exit-Fdg 0
    }
    [Console]::Error.WriteLine("FDG $version is not prepared. Run: $prepare")
    Exit-Fdg 1
}

if ($mode -ne 'hook') {
    if ($mode -eq 'fix') {
        $paths = $args | Select-Object -Skip 1
        & $tool fix --worktree @paths
    } else {
        & $tool @args
    }
    Exit-Fdg $LASTEXITCODE
}

# PowerShell's pipeline re-encodes bytes. Use streams for the hook's stdin and stdout.
$start = [Diagnostics.ProcessStartInfo]::new($tool)
$start.UseShellExecute = $false
$start.WorkingDirectory = $root
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @('hook', $event, '--host', $fdgHost)) {
    $start.ArgumentList.Add($argument)
}

try {
    $process = [Diagnostics.Process]::Start($start)
} catch {
    if ($event -eq 'session-start') { Write-Notice "FDG $version could not start. Check the .NET runtime or run: $prepare" }
    Exit-Fdg 0
}
$stdout = [IO.MemoryStream]::new()
$copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
$copyError = $process.StandardError.BaseStream.CopyToAsync([Console]::OpenStandardError())
try {
    [Console]::OpenStandardInput().CopyTo($process.StandardInput.BaseStream)
    $process.StandardInput.BaseStream.Flush()
} catch [IO.IOException] {
    # The tool can stop reading after a startup error.
} finally {
    $process.StandardInput.Close()
}
$process.WaitForExit()
[void]$copyOut.GetAwaiter().GetResult()
[void]$copyError.GetAwaiter().GetResult()
if ($event -eq 'session-start' -and $process.ExitCode -ne 0 -and $stdout.Length -eq 0) {
    Write-Notice "FDG $version could not start. Check the .NET runtime or run: $prepare"
    Exit-Fdg 0
}
$out = [Console]::OpenStandardOutput()
$stdout.WriteTo($out)
$out.Flush()
Exit-Fdg $process.ExitCode
