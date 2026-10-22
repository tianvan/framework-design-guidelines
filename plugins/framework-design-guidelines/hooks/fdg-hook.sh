#!/usr/bin/env bash
# Runs `dotnet fdg hook <event>` with the host's hook payload on stdin, or prints an install hint when the
# tool is missing. Claude Code runs it on every OS (Git Bash on Windows), Codex on macOS and Linux;
# fdg-hook.ps1 does the same for Codex on Windows.

hint='{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}'

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet fdg --version >/dev/null 2>&1 </dev/null; then
    printf '%s\n' "$hint"
    exit 0
fi

# exec hands stdin and stdout to dotnet unchanged, and its exit code back to the host.
exec dotnet fdg hook "$1"
