# Runs `dotnet fdg hook <event>` with the host's hook payload on stdin, or prints an install hint when the
# tool is missing. Codex runs it on Windows; fdg-hook.sh does the same everywhere else.
param(
    [Parameter(Mandatory = $true)]
    [string] $HookEvent
)

$hint = '{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}'

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -ne $dotnet) {
    $null | & $dotnet.Source fdg --version *> $null
}

if ($null -eq $dotnet -or $LASTEXITCODE -ne 0) {
    [Console]::Out.WriteLine($hint)
    exit 0
}

# Copies the payload as bytes: piping it through PowerShell would re-encode it and append a line break.
# dotnet writes to the inherited stdout directly, so its JSON reaches the host unchanged as well.
$startInfo = [System.Diagnostics.ProcessStartInfo]::new($dotnet.Source)
foreach ($argument in 'fdg', 'hook', $HookEvent) {
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
    # dotnet fdg stopped reading early; its exit code and output still tell the host what happened.
}

$process.WaitForExit()
exit $process.ExitCode
