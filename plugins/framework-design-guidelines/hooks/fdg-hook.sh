#!/usr/bin/env bash
# Runs `dotnet-fdg hook <event>` once with the host's hook payload on stdin, or prints a hint when the tool is
# missing or fails without output. Claude Code runs it on every OS (Git Bash on Windows), Codex on macOS and Linux;
# fdg-hook.ps1 does the same for Codex on Windows.

hint='{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}'

# Whether a dotnet tool manifest lists the dotnet-fdg package: a "dotnet-fdg" key, in any case. Package ids are
# the only keys a manifest has besides "version", "isRoot", "tools", "commands" and "rollForward", and
# a command name is an array element, not a key, so a different package with a dotnet-fdg command does not count.
# Whitespace is removed first so that a key split from its colon by a line break still matches.
manifest_lists_tool() {
    tr -d ' \t\r\n' <"$1" 2>/dev/null | grep -qi '"dotnet-fdg":'
}

# Whether the manifest is a root one, which stops dotnet's own search for manifests further up.
manifest_is_root() {
    tr -d ' \t\r\n' <"$1" 2>/dev/null | grep -qi '"isRoot":true'
}

# (a) A repository-local tool manifest pins the version: `dotnet tool run` uses it, from the current directory up.
run=()
dotnet=$(command -v dotnet 2>/dev/null)
if [ -n "$dotnet" ]; then
    dir=$PWD
    while [ -n "$dir" ]; do
        root=
        for manifest in "$dir/.config/dotnet-tools.json" "$dir/dotnet-tools.json"; do
            [ -f "$manifest" ] || continue
            if manifest_lists_tool "$manifest"; then
                run=("$dotnet" tool run dotnet-fdg)
                break 2
            fi
            if manifest_is_root "$manifest"; then
                root=1
            fi
        done
        parent=$(dirname "$dir")
        if [ -n "$root" ] || [ "$parent" = "$dir" ]; then
            break
        fi
        dir=$parent
    done
fi

# (b) The tool's own executable on the PATH, which needs no SDK resolution, unlike `dotnet fdg`. (c, d) A host
# started before the first `dotnet tool install -g` lacks the global tools directory on its PATH, so that
# directory is tried as well: under DOTNET_CLI_HOME when set, then in the user's home directory, which in Git Bash
# on Windows is USERPROFILE rather than HOME.
if [ ${#run[@]} -eq 0 ]; then
    tool=$(command -v dotnet-fdg 2>/dev/null)
    if [ -z "$tool" ]; then
        homes=()
        if [ -n "$DOTNET_CLI_HOME" ]; then
            homes+=("$DOTNET_CLI_HOME")
        fi
        case "$OSTYPE" in
            msys* | cygwin* | win32*)
                if [ -n "$USERPROFILE" ]; then
                    userprofile=$USERPROFILE
                    if command -v cygpath >/dev/null 2>&1; then
                        userprofile=$(cygpath -u "$USERPROFILE")
                    fi
                    homes+=("$userprofile")
                fi
                ;;
            *)
                if [ -n "$HOME" ]; then
                    homes+=("$HOME")
                fi
                ;;
        esac
        for home in "${homes[@]}"; do
            for candidate in "$home/.dotnet/tools/dotnet-fdg" "$home/.dotnet/tools/dotnet-fdg.exe"; do
                if [ -f "$candidate" ] && [ -x "$candidate" ]; then
                    tool=$candidate
                    break 2
                fi
            done
        done
    fi
    if [ -n "$tool" ]; then
        run=("$tool")
    fi
fi

if [ ${#run[@]} -eq 0 ]; then
    printf '%s\n' "$hint"
    exit 0
fi

# One run, with stdin passed through. Its stdout is held back until the exit code says whether it can stand alone.
out=$(mktemp 2>/dev/null) || out="${TMPDIR:-/tmp}/fdg-hook.$$.out"
err=$(mktemp 2>/dev/null) || err="${TMPDIR:-/tmp}/fdg-hook.$$.err"
trap 'rm -f "$out" "$err"' EXIT

"${run[@]}" hook "$1" >"$out" 2>"$err"
status=$?

if [ "$status" -eq 0 ] || [ -s "$out" ]; then
    cat "$out"
    exit "$status"
fi

# Failed without output, for example a dotnet-fdg too old for this plugin or one that cannot find its runtime:
# report the first stderr line, made valid UTF-8 and escaped for a JSON string.
IFS= read -r line <"$err"
if command -v iconv >/dev/null 2>&1; then
    line=$(printf '%s' "$line" | iconv -f UTF-8 -t UTF-8 -c 2>/dev/null)
else
    line=$(printf '%s' "$line" | LC_ALL=C tr -d '\200-\377')
fi
line=${line%$'\r'}
line=${line//\\/\\\\}
line=${line//\"/\\\"}
line=${line//$'\t'/\\t}
line=${line//[[:cntrl:]]/}
printf '{"systemMessage":"FDG: dotnet-fdg failed: %s"}\n' "${line:-exit code $status}"
exit 0
