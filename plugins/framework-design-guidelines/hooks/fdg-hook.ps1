# Runs `dotnet-fdg hook <event>` once with the host's hook payload on stdin, or prints a hint when the tool is
# missing or fails without output. Codex runs it on Windows; fdg-hook.sh does the same everywhere else.
param(
    [Parameter(Mandatory = $true)]
    [string] $HookEvent
)

$hint = '{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}'

# Ends the process with the given code. Codex starts this script with `pwsh -Command`, which turns any non-zero
# `exit` of a script into 1; Environment.Exit keeps the code.
function Exit-Hook([int] $Code) {
    [Console]::Out.Flush()
    [Environment]::Exit($Code)
}

function Find-Application([string] $Name) {
    Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
}

# Whether a dotnet tool manifest lists the dotnet-fdg package (package ids are case-insensitive), and whether it is
# a root manifest, which stops dotnet's own search further up. A manifest that is not valid JSON counts as neither.
function Read-Manifest([string] $Path) {
    try {
        $json = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return @{ ListsTool = $false; IsRoot = $false }
    }

    $tools = if ($json.tools -is [System.Management.Automation.PSCustomObject]) { $json.tools.PSObject.Properties.Name } else { @() }
    return @{ ListsTool = [bool]($tools | Where-Object { $_ -ieq 'dotnet-fdg' }); IsRoot = $json.isRoot -eq $true }
}

# (a) A repository-local tool manifest pins the version: `dotnet tool run` uses it, from the current directory up.
$command = $null
$arguments = @()
$dotnet = Find-Application dotnet
if ($null -ne $dotnet) {
    $dir = (Get-Location -PSProvider FileSystem).ProviderPath
    while ($dir -and $null -eq $command) {
        $isRoot = $false
        foreach ($manifest in (Join-Path $dir '.config' 'dotnet-tools.json'), (Join-Path $dir 'dotnet-tools.json')) {
            if (Test-Path -LiteralPath $manifest -PathType Leaf) {
                $info = Read-Manifest $manifest
                if ($info.ListsTool) {
                    $command = $dotnet.Source
                    $arguments = 'tool', 'run', 'dotnet-fdg'
                    break
                }

                $isRoot = $isRoot -or $info.IsRoot
            }
        }

        if ($isRoot) {
            break
        }

        $dir = [System.IO.Path]::GetDirectoryName($dir)
    }
}

# (b) The tool's own executable on the PATH, which needs no SDK resolution, unlike `dotnet fdg`. (c, d) A host
# started before the first `dotnet tool install -g` lacks the global tools directory on its PATH, so that
# directory is tried as well: under DOTNET_CLI_HOME when set, then in the user's home directory.
if ($null -eq $command) {
    $tool = Find-Application dotnet-fdg
    $homes = @($env:DOTNET_CLI_HOME, $(if ($IsWindows) { $env:USERPROFILE } else { $env:HOME })) | Where-Object { $_ }
    foreach ($userHome in $homes) {
        if ($null -ne $tool) {
            break
        }

        $tool = Find-Application (Join-Path $userHome '.dotnet' 'tools' 'dotnet-fdg')
    }

    if ($null -ne $tool) {
        $command = $tool.Source
    }
}

if ($null -eq $command) {
    [Console]::Out.WriteLine($hint)
    Exit-Hook 0
}

# One run. The payload is copied as bytes: piping it through PowerShell would re-encode it and append a line
# break. Stdout is held back as bytes until the exit code says whether it can stand alone.
$startInfo = [System.Diagnostics.ProcessStartInfo]::new($command)
foreach ($argument in $arguments + 'hook', $HookEvent) {
    $startInfo.ArgumentList.Add($argument)
}

$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$stdout = [System.IO.MemoryStream]::new()
$stderr = [System.IO.MemoryStream]::new()
try {
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $copyOut = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
    $copyErr = $process.StandardError.BaseStream.CopyToAsync($stderr)
    try {
        [Console]::OpenStandardInput().CopyTo($process.StandardInput.BaseStream)
        $process.StandardInput.Close()
    }
    catch [System.IO.IOException] {
        # dotnet-fdg stopped reading early; its exit code and output still tell the host what happened.
    }

    $process.WaitForExit()
    $copyOut.Wait()
    $copyErr.Wait()
    $status = $process.ExitCode
}
catch [System.ComponentModel.Win32Exception] {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($_.Exception.Message)
    $stderr.Write($bytes, 0, $bytes.Length)
    $status = -1
}

if ($status -eq 0 -or $stdout.Length -gt 0) {
    $out = [Console]::OpenStandardOutput()
    $stdout.WriteTo($out)
    $out.Flush()
    Exit-Hook $status
}

# Failed without output, for example a dotnet-fdg too old for this plugin or one that cannot find its runtime:
# report the first stderr line. Invalid UTF-8 becomes U+FFFD; ConvertTo-Json escapes the rest.
$text = [System.Text.UTF8Encoding]::new($false, $false).GetString($stderr.ToArray())
$line = ($text -split '\r?\n', 2)[0]
if (-not $line) {
    $line = "exit code $status"
}

$message = @{ systemMessage = "FDG: dotnet-fdg failed: $line" }
[Console]::Out.WriteLine(($message | ConvertTo-Json -Compress -EscapeHandling EscapeNonAscii))
Exit-Hook 0
