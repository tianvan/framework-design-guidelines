#!/usr/bin/env bash
# Runs the FDG version selected by the current Git worktree.
set -u
export LC_ALL=C

host='@FDG_HOST@'
receipt=".$host/hooks/fdg-install.json"
launcher=".$host/hooks/fdg/fdg.sh"
mode=${1:-}
root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
# Git Bash receives a mixed Windows path from Windows Git. Use a shell-native path for cd.
cd -- "$root" || exit 1
root=$(pwd -P)

if [[ -L ".$host" || -L ".$host/hooks" || -L "$receipt" || ! -f "$receipt" ||
      -L ".$host/hooks/fdg" || -L "$launcher" || ! -f "$launcher" ]]; then
    if [[ "$mode" == hook ]]; then exit 0; fi
    echo "FDG: no $host installation record inside the current worktree." >&2
    exit 1
fi

# The installer writes a single root-level toolVersion line. Accept the same pinned release in LF or CRLF checkouts.
version_pattern='(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*)?'
version_line="$(tr -d '\r' < "$receipt" | sed -n 's/^[[:space:]]*"toolVersion":[[:space:]]*"\([^"]*\)",[[:space:]]*$/\1/p')"
if [[ ! "$version_line" =~ ^$version_pattern$ || ${#version_line} -gt 128 ]]; then
    if [[ "$mode" == hook && ${2:-} == session-start ]]; then
        printf '%s\n' '{"systemMessage":"FDG: invalid installation record. Re-run FDG init in this worktree."}'
        exit 0
    fi
    if [[ "$mode" == hook ]]; then exit 0; fi
    echo "FDG: invalid installation record: $receipt" >&2
    exit 1
fi
version=$(printf '%s' "$version_line" | tr 'A-Z' 'a-z')

case "$(uname -s)" in
    Darwin) platform=osx ;;
    Linux) platform=linux ;;
    MINGW*|MSYS*) platform=win ;;
    *) echo "FDG: unsupported platform." >&2; exit 1 ;;
esac
arch_name=$(uname -m)
arch_fallback=
if [[ "$platform" == win ]]; then
    arch_name=${PROCESSOR_ARCHITEW6432:-${PROCESSOR_ARCHITECTURE:-$arch_name}}
    if [[ "$arch_name" == ARM64 ]]; then arch_fallback=x64; fi
    if [[ "$arch_name" == AMD64 ]]; then arch_fallback=x86; fi
elif [[ "$platform" == osx ]]; then
    if [[ "$arch_name" == x86_64 && $(sysctl -n sysctl.proc_translated 2>/dev/null) == 1 ]]; then
        arch_fallback=arm64
    elif [[ "$arch_name" == arm64 ]]; then
        arch_fallback=x64
    fi
fi
case "$arch_name" in
    x86_64|AMD64) arch=x64 ;;
    aarch64|arm64|ARM64) arch=arm64 ;;
    i?86|x86) arch=x86 ;;
    armv*) arch=arm ;;
    *) echo "FDG: unsupported architecture." >&2; exit 1 ;;
esac

cache=${FDG_TOOL_CACHE:-}
suffix=
if [[ "$platform" == win ]]; then
    cache=${cache:-"${LOCALAPPDATA:-}/FDG/tools"}
    cache=$(cygpath -u "$cache")
    suffix=.exe
elif [[ -z "$cache" ]]; then
    case "${XDG_CACHE_HOME:-}" in
        /*) cache="$XDG_CACHE_HOME/fdg/tools" ;;
        *) cache="$HOME/.cache/fdg/tools" ;;
    esac
fi
tool=
if [[ "$cache" == /* ]]; then
    for candidate_arch in "$arch" ${arch_fallback:+"$arch_fallback"}; do
        platform_cache=$(cd -P -- "$cache/$platform-$candidate_arch" 2>/dev/null && pwd -P) || continue
        case "$platform_cache/" in "$root"/*) continue ;; esac
        candidate="$platform_cache/$version/dotnet-fdg$suffix"
        if [[ -x "$candidate" && ! -L "$platform_cache/$version" && ! -L "$candidate" ]]; then
            tool="$candidate"
            break
        fi
    done
fi

case "$mode" in
    prepare)
        if [[ -x "$tool" ]] && [[ $("$tool" --version 2>/dev/null) == "$version_line" ]]; then
            printf '%s\n' "FDG $version_line is ready."
            exit 0
        fi
        # Only this explicit command uses tool exec. The CLI installs and validates a versioned copy.
        exec dotnet tool exec --yes "dotnet-fdg@$version_line" -- cache prepare --host "$host"
        ;;
    hook)
        if [[ ! -x "$tool" ]]; then
            if [[ ${2:-} == session-start ]]; then
                printf '{"systemMessage":"FDG %s is not prepared. From the repository root, run: bash %s prepare"}\n' "$version_line" "$launcher"
            fi
            exit 0
        fi
        # Leave stdout and stdin untouched. On SessionStart only, turn a launch failure into a visible diagnostic.
        if [[ ${2:-} != session-start ]]; then
            exec "$tool" hook "${2:-}" --host "$host"
        fi
        out=$("$tool" hook session-start --host "$host")
        status=$?
        if [[ $status -ne 0 && -z "$out" ]]; then
            printf '{"systemMessage":"FDG %s could not start. Check the .NET runtime or run: bash %s prepare"}\n' "$version_line" "$launcher"
            exit 0
        fi
        printf '%s' "$out"
        exit "$status"
        ;;
    fix|project|uninstall|--version)
        if [[ ! -x "$tool" ]]; then
            echo "FDG $version_line is not prepared. Run: bash $launcher prepare" >&2
            exit 1
        fi
        if [[ "$mode" == fix ]]; then
            shift
            exec "$tool" fix --worktree "$@"
        fi
        exec "$tool" "$@"
        ;;
    *)
        echo "Usage: bash $launcher prepare|fix|project|uninstall|--version" >&2
        exit 2
        ;;
esac
