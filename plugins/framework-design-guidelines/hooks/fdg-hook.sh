#!/usr/bin/env bash
# Runs `dotnet-fdg hook <event>` with the host's hook payload on stdin, or prints a hint when the tool is
# missing or does not start. Claude Code runs it on every OS (Git Bash on Windows), Codex on macOS and Linux;
# fdg-hook.ps1 does the same for Codex on Windows.

hint='{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}'

# The tool's own executable needs no SDK resolution, unlike `dotnet fdg`. A host started before the first
# `dotnet tool install -g` lacks ~/.dotnet/tools on its PATH, so that directory is tried as well.
tool=$(command -v dotnet-fdg 2>/dev/null)
if [ -z "$tool" ]; then
    for candidate in "$HOME/.dotnet/tools/dotnet-fdg" "$HOME/.dotnet/tools/dotnet-fdg.exe"; do
        if [ -f "$candidate" ] && [ -x "$candidate" ]; then
            tool=$candidate
            break
        fi
    done
fi

if [ -z "$tool" ]; then
    printf '%s\n' "$hint"
    exit 0
fi

error=$("$tool" --version 2>&1 >/dev/null </dev/null)
status=$?
if [ "$status" -ne 0 ]; then
    # The first stderr line, escaped for a JSON string.
    line=${error%%$'\n'*}
    line=${line%$'\r'}
    line=${line//\\/\\\\}
    line=${line//\"/\\\"}
    line=${line//$'\t'/\\t}
    line=${line//[[:cntrl:]]/}
    printf '{"systemMessage":"FDG: dotnet-fdg failed to start: %s"}\n' "${line:-exit code $status}"
    exit 0
fi

# exec hands stdin and stdout to the tool unchanged, and its exit code back to the host.
exec "$tool" hook "$1"
