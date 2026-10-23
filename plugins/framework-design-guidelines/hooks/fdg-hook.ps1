# Runs `dotnet-fdg hook <event>` with the host's hook payload on stdin, or prints a hint when the tool is
# missing or does not start. Codex runs it on Windows; fdg-hook.sh does the same everywhere else.
param(
    [Parameter(Mandatory = $true)]
    [string] $HookEvent
)

$hint = '{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}'

# The tool's own executable needs no SDK resolution, unlike `dotnet fdg`. A host started before the first
# `dotnet tool install -g` lacks the global tools directory on its PATH, so that directory is tried as well.
$tool = Get-Command dotnet-fdg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $tool) {
    $userHome = if ($IsWindows) { $env:USERPROFILE } else { $env:HOME }
    if ($userHome) {
        $fallback = Join-Path $userHome '.dotnet' 'tools' 'dotnet-fdg'
        $tool = Get-Command $fallback -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    }
}

if ($null -eq $tool) {
    [Console]::Out.WriteLine($hint)
    exit 0
}

$check = [System.Diagnostics.ProcessStartInfo]::new($tool.Source)
$check.ArgumentList.Add('--version')
$check.UseShellExecute = $false
$check.RedirectStandardInput = $true
$check.RedirectStandardOutput = $true
$check.RedirectStandardError = $true
try {
    $version = [System.Diagnostics.Process]::Start($check)
    $version.StandardInput.Close()
    $null = $version.StandardOutput.ReadToEndAsync()
    $stderr = $version.StandardError.ReadToEnd()
    $version.WaitForExit()
    $status = $version.ExitCode
}
catch [System.ComponentModel.Win32Exception] {
    $stderr = $_.Exception.Message
    $status = -1
}

if ($status -ne 0) {
    $line = ($stderr -split '\r?\n', 2)[0]
    if (-not $line) {
        $line = "exit code $status"
    }

    $message = @{ systemMessage = "FDG: dotnet-fdg failed to start: $line" }
    [Console]::Out.WriteLine(($message | ConvertTo-Json -Compress -EscapeHandling EscapeNonAscii))
    exit 0
}

# Copies the payload as bytes: piping it through PowerShell would re-encode it and append a line break.
# The tool writes to the inherited stdout directly, so its JSON reaches the host unchanged as well.
$startInfo = [System.Diagnostics.ProcessStartInfo]::new($tool.Source)
foreach ($argument in 'hook', $HookEvent) {
    $startInfo.ArgumentList.Add($argument)
}

$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$process = [System.Diagnostics.Process]::Start($startInfo)
try {
    [Console]::OpenStandardInput().CopyTo($process.StandardInput.BaseStream)
    $process.StandardInput.Close()
}
catch [System.IO.IOException] {
    # dotnet-fdg stopped reading early; its exit code and output still tell the host what happened.
}

$process.WaitForExit()
exit $process.ExitCode
