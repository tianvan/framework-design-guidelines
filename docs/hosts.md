# Host hook behavior

What `dotnet fdg hook ...` relies on in the two hosts, measured on Windows 11 with Claude Code 2.1.303 and
Codex CLI 0.163.0 (2026-10-22), plus what the Codex rust-v0.163.0 source says where a measurement was not
possible. Task 10 re-checked this with the plugin loaded; see "Real-host checks" below.

## How the input was captured

- **Claude Code**: `claude -p --model haiku --plugin-dir <temp plugin>` in a temporary git repository, prompt
  "Reply with the single word: hello". The temporary plugin had only `.claude-plugin/plugin.json` and
  `hooks/hooks.json`; each of SessionStart, UserPromptSubmit and Stop ran a script that saved stdin to a file. The
  first Stop answered `{"decision":"block","reason":"Reply with the single word: done"}` to capture a second Stop.
  The user's settings and marketplaces were not touched.
- **Codex**: `codex exec --ephemeral --dangerously-bypass-hook-trust -c "hooks.<Event>=[{hooks=[{type='command',command='...',timeout=30}]}]"`
  in a temporary git repository, same prompt and same blocking first Stop. `~/.codex` was not modified and no
  credentials were copied: `-c` adds a session-flags config layer, and `--dangerously-bypass-hook-trust` runs
  hooks that have not been reviewed, for that invocation only.

The payloads, with personal paths replaced by `C:\Users\user\...`, are in `tests/fixtures/hooks/{claude,codex}/`:
`session-start.json`, `user-prompt-submit.json`, `stop.json` and `stop-after-block.json` (the Stop that follows
a block). All Codex fixtures are measured, not derived from the source.

## Input fields

Both hosts send `session_id`, `cwd`, `hook_event_name` and `transcript_path` (Codex: `null` with `--ephemeral`).

| Field | Claude Code | Codex |
|---|---|---|
| SessionStart `source` | `startup` (docs: also `resume`, `clear`, `compact`) | `startup` (schema: also `resume`, `clear`, `compact`, `fork`) |
| SessionStart `model`, `permission_mode` | absent | present |
| Turn id on UserPromptSubmit and Stop | `prompt_id` | `turn_id` |
| `stop_hook_active` on Stop | `false`, then `true` after a block | `false`, then `true` after a block |
| Session id format | UUID v4 | UUID v7 |

`HookInput.Parse` tells the hosts apart by these fields: `turn_id` means Codex, `prompt_id` means Claude, and on
SessionStart `model` together with `permission_mode` means Codex. When none of them is present it checks the
environment: Codex sets `PLUGIN_ROOT` for plugin hooks, Claude does not. **`CLAUDE_PLUGIN_ROOT` cannot tell the
hosts apart**: Codex also sets it (and `CLAUDE_PLUGIN_DATA`), "for OOTB compat with existing plugins"
(`codex-rs/hooks/src/engine/discovery.rs`).

`stop_hook_active` is the "already blocked" signal both hosts provide. `dotnet fdg` does not rely on it: it counts
blocks per turn itself in `blocks.txt` (reset by UserPromptSubmit), because the limit is 2 blocks and the field
only says "at least one".

## Shell that runs hook commands

- **Claude Code on Windows**: Git Bash (`$0` was `/usr/bin/bash`, bash 5.3, `MSYSTEM` set). The docs say: `sh -c`
  on macOS and Linux, Git Bash on Windows, PowerShell when Git Bash is not installed; a hook's `shell` field
  (`"bash"` or `"powershell"`) chooses explicitly.
- **Codex on Windows**: the session's shell, which on Windows is PowerShell. Measured command line:
  `"C:\Program Files\PowerShell\7\pwsh.dll" -NoProfile -Command "<command>"`. Without a session shell it falls back
  to `%COMSPEC% /C` (cmd.exe); on Unix to `$SHELL -lc` or `/bin/sh` (`codex-rs/hooks/src/engine/command_runner.rs`,
  `codex-rs/core/src/session/mod.rs`). A bash script therefore has to be started explicitly
  (`& "C:\Program Files\Git\usr\bin\bash.exe" script.sh` worked). Codex's hook config also accepts
  `commandWindows` next to `command` (`codex-rs/config/src/hook_config.rs`), which Task 8 can use for a
  Windows-specific command line.

## Timeout

Both hosts: field `timeout`, in **seconds**, per hook command.

- Claude Code: default 600 s for command hooks, 30 s for UserPromptSubmit (docs).
- Codex: default 600 s, minimum 1 s; SessionEnd and Interrupt default to 1 s, capped at 3 s
  (`normalize_command_hook` in `discovery.rs`).

`dotnet fdg` enforces its own Stop timeout (`FDG_HOOK_TIMEOUT_SECONDS`, default 60 s), so the host timeout in
`hooks.json` must be larger, for example 90 s, or the host kills the hook before it can report the timeout.

## Exit codes and output

- Exit 0 with empty stdout: nothing happens. Exit 0 with JSON: `decision`/`reason`, `systemMessage` and
  `hookSpecificOutput.additionalContext` are honored as documented.
- Exit 2: blocking, with stderr as the reason (both hosts).
- Any other exit code: a **non-blocking** error in both hosts. Claude shows it and continues (docs: "non-blocking
  error"); Codex marks the hook run `Failed` and continues (`hook exited with code N`, `events/stop.rs`).

`dotnet fdg hook ...` exits 1 only when stdin is not a hook payload, which both hosts treat as a non-blocking
failure, and 0 otherwise. Unexpected errors become `{"systemMessage":"FDG hook error: ..."}` with exit 0.

Codex's Stop output schema has no `hookSpecificOutput`, so Stop can only block with `reason` or show a
`systemMessage`; SessionStart and UserPromptSubmit accept `additionalContext` (`codex-rs/hooks/schema/generated/`).

## Codex hook approval (design §8)

Codex runs a hook from a user or project config layer, or from a plugin, only when it is trusted: its
`trusted_hash` in the hook state must equal the hash of the hook's current definition
(`hook_trust_status` in `discovery.rs`). A new hook is `Untrusted` ("New hook - review required" in the TUI's
hooks browser) and a changed one is `Modified`; neither runs until the user reviews it, and **they are skipped
silently**: measured with `codex exec` and a `-c` hook without `--dangerously-bypass-hook-trust`, the hook did not
run and nothing was printed. Managed hooks (system, MDM, enterprise) are trusted without review.

Consequences for the plugin (Task 8/10): after installing, and again after any plugin update that changes
`hooks.json`, the user must approve the hooks in Codex (`/hooks` in the TUI) or they silently do nothing. The
install documentation has to say so; SessionStart cannot warn about it, since it is itself a hook.

**Upgrading from 0.2.0**: phase 3 changes every `command` and `commandWindows` line in `hooks.codex.json`, in one
release (see "The `command` line" and "The `commandWindows` line" below), so after updating the plugin Codex lists
the three hooks as `Modified` and the user has to approve them once more in `/hooks`, on every OS.

## Plugin layout for both hosts (Task 10)

One plugin directory, `plugins/framework-design-guidelines/`, serves both hosts:

| File | Read by |
|---|---|
| `.claude-plugin/plugin.json` | Claude Code |
| `.codex-plugin/plugin.json` | Codex |
| `hooks/hooks.json` | Claude Code only |
| `hooks/hooks.codex.json` | Codex only, through the `hooks` field of `.codex-plugin/plugin.json` |
| `hooks/fdg-hook.sh` | Claude Code on every OS, Codex on macOS and Linux |
| `hooks/fdg-hook.ps1` | Codex on Windows |
| `.claude-plugin/marketplace.json` (repository root) | the marketplace commands of both hosts (Codex: see "Codex plugin install" below) |

### Why Codex gets its own hooks file

Codex runs a hook command through the session shell, which is PowerShell on Windows, so it cannot use the
`bash "${CLAUDE_PLUGIN_ROOT}/..."` line that Claude needs. Evidence from the openai/codex `rust-v0.163.0` source:

- **The manifest can point to a hooks file.** `RawPluginManifest` has `hooks: Option<RawPluginManifestHooks>`,
  which accepts a `./`-relative path, a list of paths, or inline hooks (`codex-rs/core-plugins/src/manifest.rs`,
  `resolve_manifest_hooks`).
- **That path replaces the default.** `load_plugin_hooks` reads the manifest's hooks when present and
  `hooks/hooks.json` only otherwise (`codex-rs/core-plugins/src/loader.rs`, `DEFAULT_HOOKS_CONFIG_FILE`).
- **The Codex manifest wins over the Claude one.** Codex looks for `.codex-plugin/plugin.json`, then
  `.claude-plugin/plugin.json`, then `.cursor-plugin/plugin.json` (`DISCOVERABLE_PLUGIN_MANIFEST_PATHS` in
  `codex-rs/exec-server-protocol/src/protocol.rs`). A root `plugin.json` would come first, but only if it declares
  the Agent Plugins schema (`find_plugin_manifest_path` in `codex-rs/utils/plugins/src/plugin_namespace.rs`).
- **The plugin root is substituted in the command text.** For plugin hooks Codex sets `PLUGIN_ROOT`,
  `CLAUDE_PLUGIN_ROOT`, `PLUGIN_DATA` and `CLAUDE_PLUGIN_DATA`, and replaces every `${NAME}` of them in the command
  string before running it (`codex-rs/hooks/src/engine/discovery.rs`: lines 262-270 put the variables into the
  hook's environment, lines 569-570 replace only the braced form `${KEY}`). So `"${PLUGIN_ROOT}/hooks/fdg-hook.ps1"`
  would become a literal path, also under PowerShell, where `${PLUGIN_ROOT}` alone would be an undefined variable.
  The plugin no longer relies on this substitution (see below): a substituted path is parsed by the shell.
- **`commandWindows` replaces `command` on Windows** (same file: `command_windows.unwrap_or(command)` under
  `cfg!(windows)`; the field is `commandWindows`, alias `command_windows`, in `codex-rs/config/src/hook_config.rs`).

Claude Code reads only `.claude-plugin/plugin.json` and `hooks/hooks.json`, so it never sees `hooks.codex.json`.
Option (a) of the Task 10 ruling therefore applies: `.codex-plugin/plugin.json` sets
`"hooks": "./hooks/hooks.codex.json"`, whose entries have `command` set to
`bash "$PLUGIN_ROOT/hooks/fdg-hook.sh" <event>` and `commandWindows` set to the line below. Option (b),
`commandWindows` in the shared `hooks.json`, would also pass `claude plugin validate --strict`; it was not needed.

### The `commandWindows` line

```
pwsh -NoProfile -ExecutionPolicy Bypass -Command "& (Join-Path ([Environment]::GetEnvironmentVariable('PLUGIN_ROOT')) 'hooks/fdg-hook.ps1') <event>"
```

Up to 0.2.0 it was `pwsh -NoProfile -ExecutionPolicy Bypass -File "${PLUGIN_ROOT}/hooks/fdg-hook.ps1" <event>`.
Codex substitutes the plugin root into that text, and the session shell then parses it: in PowerShell a `$` or a
backtick in the path (for example in a user name) is expanded inside the double quotes, and a `"` ends the
string. The current line contains no `$`, no backtick and no `${PLUGIN_ROOT}`, so Codex substitutes nothing and
both session shells, PowerShell and the `cmd.exe` fallback, hand it to `pwsh` unchanged. The inner `pwsh` reads
the path from the `PLUGIN_ROOT` environment variable, which Codex sets for plugin hooks, so the path is never
parsed as code. `HookShellTests.Codex_windows_command_survives_special_characters` runs the line under
`pwsh -NoProfile -Command` and `cmd /d /s /c` with the plugin root ``a b$c`d'e 中文`` and checks that the payload
reaches the tool and its stdout comes back byte for byte.

`pwsh` starts its own PowerShell, so the command works whether the session shell is PowerShell or `cmd.exe`, and
`-ExecutionPolicy Bypass` keeps a machine policy such as `AllSigned` from blocking the unsigned script. It needs
PowerShell 7 (`pwsh`) on the PATH. Under `-Command` a script's `exit N` leaves `pwsh` with exit code 1, so
`fdg-hook.ps1` ends with `[Environment]::Exit`, which keeps the tool's exit code for whoever started this `pwsh`.
That matters only for the `cmd.exe` fallback and for a host that runs the line without a session shell: a
PowerShell session shell around the line still turns any non-zero exit code into 1. Neither case changes what the
hooks do, because `dotnet-fdg hook` itself exits only 0 or 1 and signals a block through its stdout JSON
(`decision`), which passes through unchanged. `HookShellTests.Codex_windows_command_keeps_the_tool_exit_code`
pins both behaviours.

### The `command` line

```
bash "$PLUGIN_ROOT/hooks/fdg-hook.sh" <event>
```

Up to 0.2.0 it was `bash "${PLUGIN_ROOT}/hooks/fdg-hook.sh" <event>`. Codex replaced `${PLUGIN_ROOT}` in the text,
and `$SHELL -lc` then parsed the path inside the double quotes: `$name` expanded (`a$c` became `a`), a backtick
started a command substitution, `\` escaped and a `"` ended the string. A root containing `` $c`d"e `` failed
with a syntax error and exit 2, which Codex treats as blocking. Codex replaces only the braced `${KEY}` and also puts
`PLUGIN_ROOT` into the hook's environment (`discovery.rs`, above), so the unbraced `$PLUGIN_ROOT` reaches the shell
untouched and the shell expands it from the environment. A value expanded inside double quotes is not parsed again.
`HookShellTests.Codex_unix_command_survives_special_characters` applies Codex's substitution to each event's
`command`, runs it with `sh -c` and `bash -c` with a plugin root containing a space, `$`, a backtick, `'`, CJK
characters and (except on Windows, whose file names cannot hold it) `"`, and checks that the payload reaches the
tool and its stdout comes back byte for byte.

Claude's `hooks.json` still uses `bash "${CLAUDE_PLUGIN_ROOT}/hooks/fdg-hook.sh" <event>`, unchanged.

### The wrappers

Both wrappers run the tool's own executable, `dotnet-fdg`, not `dotnet fdg`: the `dotnet` muxer first resolves an
SDK from the `global.json` of the current directory, so `dotnet fdg` fails where that file pins an SDK that is not
installed (measured by the Task 10 review: exit 155), and it costs about 0.3 s against 0.07 s per call. The
exception is a repository that pins the tool. In order, they use:

1. a local tool manifest, `.config/dotnet-tools.json` or `dotnet-tools.json` in the current directory or above it,
   that lists the `dotnet-fdg` package (the key under `tools`, in any case): `dotnet tool run dotnet-fdg`, so the
   repository's pinned version runs. The search stops at a manifest with `"isRoot": true`, as dotnet's does.
   `fdg-hook.sh` does not parse JSON (jq may be missing): it removes whitespace and looks for the key
   `"dotnet-fdg":`, so a different package whose command is called `dotnet-fdg` (an array element) does not count;
2. `dotnet-fdg` on the PATH;
3. `$DOTNET_CLI_HOME/.dotnet/tools`, when `DOTNET_CLI_HOME` is set;
4. `%USERPROFILE%\.dotnet\tools` on Windows (also for `fdg-hook.sh` under Git Bash, where `HOME` can differ),
   `~/.dotnet/tools` elsewhere. 3 and 4 cover a host whose PATH predates the first `dotnet tool install -g`.

They start the tool once per hook: there is no `--version` probe any more, since the hook entry point checks the
version and the protocol itself. The hook payload goes to its stdin byte for byte (`fdg-hook.ps1` copies the raw
stdin stream, since piping it through PowerShell would re-encode it and append a line break), and its stdout is
held back until it exits:

- Nothing found: they print
  `{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}` and exit 0.
- Exit 0: its stdout, unchanged, and exit 0.
- Non-zero exit with output: its stdout, unchanged, and its exit code.
- Non-zero exit without output, for example a tool too old to know `hook`, one that cannot find its runtime, or
  `dotnet tool run` failing on a `global.json` SDK that is not installed: they print
  `{"systemMessage":"FDG: dotnet-fdg failed: <first line of its stderr>"}` and exit 0, so the failure is never
  silent. The line is made valid UTF-8 first: `fdg-hook.sh` uses `iconv -f UTF-8 -t UTF-8 -c` when available and
  otherwise drops every byte from 0x80 to 0xFF; `fdg-hook.ps1` decodes it as UTF-8 with U+FFFD for invalid bytes.
  Then it is escaped for JSON (`fdg-hook.sh` escapes quotes, backslashes and tabs and drops other control
  characters; `fdg-hook.ps1` uses `ConvertTo-Json`).

`.gitattributes` keeps `*.sh` at LF line endings: with `core.autocrlf=true`, the Git for Windows default, a CRLF
checkout fails in bash with `command not found` on `$'\r'`, and Claude Code installs marketplace plugins with git.

## Real-host checks (Task 10)

Windows 11, Claude Code 2.1.303, Codex CLI 0.163.0, 2026-10-22. `dotnet-fdg` 0.1.0 was packed into
`artifacts/packages` and installed with `--tool-path artifacts/tools`; that directory was put at the front of
`PATH` for the test commands only. The sample was a temporary git repository outside this one, with a space and
CJK characters in its path, holding a netstandard2.0, C# 7.3 project with `FrameworkDesignGuidelinesProfile=library`
that references the packed analyzer package, built once before the sessions. The user's Claude settings, Codex
config, marketplaces and credentials were not touched.

### Claude Code

`claude -p --model haiku --plugin-dir <repo>/plugins/framework-design-guidelines --permission-mode acceptEdits
--output-format stream-json --verbose --include-hook-events "<prompt>"`, with a prompt asking for a try/catch whose
catch clause is written on one line.

- **SessionStart**: `SessionStart:startup` returned the `additionalContext` (`This repository follows Framework
  Design Guidelines (3rd ed.).` ... `Sample/** = library. C# 7.3; targets netstandard2.0.` ...), and the model quoted
  it in full.
- **Stop**: ran `fdg-hook.sh` under Git Bash, exit 0, no output. Afterwards the catch clause was expanded to four
  lines: the auto-fix happened and nothing was left to report.
- **Tool missing** (`artifacts/tools` not on the PATH, no global tool): SessionStart, UserPromptSubmit and Stop each
  printed the install hint with exit 0. The stream showed `UserPromptSubmit says: FDG: dotnet-fdg is not
  installed...` and `Stop says: ...`; the SessionStart `systemMessage` did not appear as a stream event in `-p`
  mode.
- **User commands**: the init event lists the skills as `framework-design-guidelines:fdg-fix`, `...:fdg-review`,
  `...:fdg-setup` and `...:fdg-write`, so the full command is `/framework-design-guidelines:fdg-fix`. In `-p` mode
  `/fdg-fix Sample/Parser.cs` started the fdg-fix workflow too, since the short name is unique.
- **Compaction**: `claude -p --resume <session> "/compact"` ran `SessionStart:resume` and, after compacting,
  `SessionStart:compact`; both returned the FDG `additionalContext`. In Git Bash this needs `MSYS_NO_PATHCONV=1`,
  or `/compact` is rewritten to `C:/Program Files/Git/compact`.

### Codex

`codex exec --ephemeral --json -s workspace-write --dangerously-bypass-hook-trust` with one
`-c "hooks.<Event>=[{hooks=[{type='command',command='...',commandWindows='...',timeout=<15|90>}]}]"` for each of
SessionStart, UserPromptSubmit and Stop, holding the command lines of `hooks.codex.json` with the plugin root
written out: `-c` hooks are not plugin hooks, so `${PLUGIN_ROOT}` is not substituted for them.

- **The wrapper runs under pwsh**: Codex took `commandWindows` on Windows. The session's UserPromptSubmit and Stop
  wrote their state under `.git/fdg/<thread id>/` (`snapshot.json`, then `ai/`, `formatted/`, `sarif/`,
  `baseline/` and `ai-lines.json`); Stop with its build took about 28 seconds, within the 90-second timeout.
- **SessionStart context comes back**: in one run the model quoted the context's last line ("Before designing or
  changing types, members, or public APIs, use the fdg-write skill..."); in another it declined to quote hidden
  context verbatim but answered "The repository instructions require `fdg-write` before changing a member" and read
  the fdg-write skill.
- Codex's exact process, `pwsh -NoProfile -Command "pwsh -NoProfile -ExecutionPolicy Bypass -File
  \"...fdg-hook.ps1\" session-start"`, fed the Codex `session-start.json` fixture as UTF-8 with a CJK `cwd`,
  printed the full `hookSpecificOutput` JSON and exited 0.
- In both runs the model expanded the catch clause itself, following the context and the skill, so a Codex Stop
  auto-fix was not observed: the Stop's `ai/` and `formatted/` copies were equal.

### Codex plugin install (Task 10 review)

The Task 10 review installed the plugin into an isolated `CODEX_HOME` under %TEMP% (deleted afterwards; `~/.codex`
untouched), with Codex CLI 0.163.0:

- `codex plugin marketplace add <repository>` worked, and `codex plugin marketplace list` and `codex plugin list`
  showed that Codex read the repository's `.claude-plugin/marketplace.json`.
- `codex plugin add framework-design-guidelines@framework-design-guidelines` installed the plugin to
  `plugins/cache/.../0.1.0` under `CODEX_HOME`; `codex plugin list` reported it `installed, enabled 0.1.0`.
- The `skills` path of `.codex-plugin/plugin.json` works: `codex debug prompt-input` listed the skills as
  `framework-design-guidelines:fdg-fix`, `...:fdg-review`, `...:fdg-setup` and `...:fdg-write`.

## Still to confirm (pilot, Task 12)

The [1.0 verification](#10-verification) below covers each of these.

- **Codex hooks from the plugin**: that Codex loads `hooks.codex.json` through the `hooks` field of
  `.codex-plugin/plugin.json`, and that it sets `PLUGIN_ROOT` in the environment of those commands, which both
  command lines now read. Both are known from the source only, since `-c` hooks are not plugin hooks.
- **Codex `/hooks` approval** of the plugin's hooks, after install and after an update that changes them. Not done
  here, because it writes the user's hook trust state.
- **Codex skill mention form**: whether `$fdg-setup` resolves, or only the qualified
  `framework-design-guidelines:fdg-setup`.
- **Claude marketplace install**: `claude plugin marketplace add` and `claude plugin install`, deferred because
  they change the user's settings; `--plugin-dir` loaded the same directory.
- **Codex and compaction**: whether SessionStart runs with `source: "compact"` after a Codex compaction. The schema
  lists `compact`, but `codex exec` offers no cheap way to trigger one.
- **Claude after compaction**: the hook output is confirmed above; that the model then sees the context again
  follows from the documentation and was not checked separately.

## 1.0 verification

Run after 1.0.0-rc.1 is released, with the released bits: both packages from nuget.org and the plugin from the
GitHub marketplace (part A first installs 0.2.0 from a clone at its tag, to test the update). Everything happens in a
new temporary repository with a small class library, a test project and seeded violations. It is never pushed, and
you decide afterwards whether to delete it. Do not use the pilot clone or an internal repository.

Rider is not installed on the verification machine, so it stays **not verified**.

### Set up the temporary repository

In PowerShell 7, with .NET SDK 10.0.100 or later and git. The script onboards the class library (`library`) and
the test project (`test`) with the release candidate, so the hooks are active from the first Codex session. It
also adds a console project, `tools/Shipping.Tools`, that is left without the package, for `/fdg-setup` in part B.

```powershell
$version = '1.0.0-rc.1'
$repo = Join-Path $env:TEMP 'fdg-verify\repo'
New-Item -ItemType Directory $repo -Force | Out-Null
Set-Location $repo
git init -q
dotnet new gitignore
dotnet new sln -n Shipping
dotnet new classlib -n Shipping -o src/Shipping
dotnet new xunit -n Shipping.Tests -o tests/Shipping.Tests
dotnet new console -n Shipping.Tools -o tools/Shipping.Tools
dotnet sln add src/Shipping tests/Shipping.Tests tools/Shipping.Tools
dotnet add tests/Shipping.Tests reference src/Shipping
Remove-Item src/Shipping/Class1.cs, tests/Shipping.Tests/UnitTest1.cs

# Onboard src/ and tests/, as /fdg-setup would; tools/ stays without the package.
foreach ($pair in @(@('src', 'library'), @('tests', 'test')))
{
@"
<Project>
  <PropertyGroup>
    <FrameworkDesignGuidelinesProfile>$($pair[1])</FrameworkDesignGuidelinesProfile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FrameworkDesignGuidelines.Analyzers" Version="$version" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $pair[0] 'Directory.Build.props')
}

# Seeded violations. They are committed before the session, so they count as existing code.
# Order.cs: CA1051, a visible instance field.
@'
namespace Shipping;

/// <summary>An order.</summary>
public class Order
{
    /// <summary>The weight in kilograms.</summary>
    public decimal weight;
}
'@ | Set-Content src/Shipping/Order.cs

# OrderBatch.cs: CA1710, reported only with the package's exclude_indirect_base_types = false.
@'
using System.Collections.ObjectModel;

namespace Shipping;

/// <summary>Orders on one truck.</summary>
public class OrderBatch : Collection<Order>
{
}
'@ | Set-Content src/Shipping/OrderBatch.cs

# Quantity.cs: FDGA003, a catch clause on one line.
@'
using System;
using System.Globalization;

namespace Shipping;

/// <summary>Parses quantities.</summary>
public static class Quantity
{
    /// <summary>Parses a quantity, or returns 0.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The quantity.</returns>
    public static int Parse(string text)
    {
        try { return int.Parse(text, CultureInfo.InvariantCulture); } catch (FormatException) { return 0; }
    }
}
'@ | Set-Content src/Shipping/Quantity.cs

# QuantityTests.cs: an underscore in a method name, fine in the test profile (no CA1707).
@'
using Shipping;
using Xunit;

namespace Shipping.Tests;

public class QuantityTests
{
    [Fact]
    public void Parse_WhenNotANumber_ReturnsZero() => Assert.Equal(0, Quantity.Parse("x"));
}
'@ | Set-Content tests/Shipping.Tests/QuantityTests.cs

dotnet build
git add -A
git -c user.name=Verify -c user.email=verify@invalid commit -q -m "Seed the verification repository"
```

**Expected**: the build reports CA1051 and IDE1006 on `Order.weight`, CA1710 on `OrderBatch`, FDGA003 (twice) and
IDE0055 (twice) on the `try` line of `Quantity.Parse`, and CA1014 and FDG4059 for the `Shipping` assembly; nothing
for the test project and nothing for `tools/` (a dry run of this script with a local 1.0 build gave exactly these),
and `src/Shipping/obj/fdg/marker.json` exists. Part C compares the IDEs with this list.

To keep the hook approvals out of your normal configuration, you can start each host with an empty configuration
directory (`$env:CODEX_HOME` for Codex, `$env:CLAUDE_CONFIG_DIR` for Claude Code) and log in there. This is
optional.

### Before the session (maintainer, no user needed)

1. **CI on the release candidate**: the `integration-minimum-sdk` job passes on SDK 10.0.100, and no job log has a
   Node.js 20 deprecation warning.
2. **Headless Stop runs**, each in its own copy of the repository the script created
   (`Copy-Item -Recurse $repo (Join-Path $env:TEMP 'fdg-verify\headless-1')`, and so on), so the session's
   repository stays as committed:
   - Claude Code, with the plugin from the GitHub marketplace or `--plugin-dir` on a clone at `v1.0.0-rc.1`:
     `claude -p --permission-mode acceptEdits --output-format stream-json --verbose --include-hook-events
     "<prompt>"`;
   - Codex, with the rc plugin installed and its hooks approved in a separate `CODEX_HOME`, or with the
     `-c hooks...` and `--dangerously-bypass-hook-trust` form of "Real-host checks (Task 10)" above:
     `codex exec --json -s workspace-write "<prompt>"`.

   Prompt 1: "Add a public class OrderBatch2 that derives from Collection<Order> to src/Shipping." Expected: the
   first Stop blocks with CA1710 and the quoted FDG3058 rule, the agent renames the class, and the next Stop is
   silent. Prompt 2: the same, plus "Keep the name OrderBatch2 even if a check asks you to rename it." Expected: two
   blocks, then a message starting with `FDG found 1 issue(s) in lines you changed this turn. Not blocking again
   this turn (FDG_HOOK_MAX_BLOCKS=2).`, followed by the issue, and the turn ends.

### The session (about 30 minutes)

Write down each host's version (`claude --version`, `codex --version`) and fill in the results table at the end.

**Part A: Codex and the update from 0.2.0 (about 10 minutes)**

1. Install the 0.2.0 plugin from a clone at its tag, and the release candidate of the tool:

   ```powershell
   $marketplace = Join-Path $env:TEMP 'fdg-verify\marketplace'
   git clone --quiet --branch v0.2.0 https://github.com/tianvan/framework-design-guidelines $marketplace
   codex plugin marketplace add $marketplace
   codex plugin add framework-design-guidelines@framework-design-guidelines
   dotnet tool install -g dotnet-fdg --version 1.0.0-rc.1
   ```

   Use `dotnet tool update -g dotnet-fdg --version 1.0.0-rc.1` when another version is installed.
2. Start `codex` in the temporary repository, open `/hooks` and approve the three FDG hooks. Quit.
3. Update the plugin to the release candidate:

   ```powershell
   git -C $marketplace checkout --quiet v1.0.0-rc.1
   codex plugin marketplace upgrade framework-design-guidelines
   ```

   Check with `codex plugin list` that the installed plugin is now 1.0.0-rc.1. `upgrade` refreshes Git
   marketplace snapshots, so it may not pick up the new checkout of a local one. If the plugin is still 0.2.0, run
   `codex plugin remove framework-design-guidelines@framework-design-guidelines` and
   `codex plugin marketplace remove framework-design-guidelines`, add the marketplace and the plugin again as in
   step 1, and note this in the results. Step 4 approves the hooks either way.
4. Start `codex` in the temporary repository and open `/hooks`.
   **Expected (re-approval)**: the three FDG hooks are listed as modified (or, after the fallback in step 3, as
   new) and need review. Approve them.
5. Send "Reply with the single word: hello".
   **Expected (plugin hooks and `PLUGIN_ROOT`)**: no hook error, and `.git/fdg/<session id>/snapshot.json` exists in
   the repository (written by UserPromptSubmit; the directory is named after the Codex session id). This shows
   that Codex loaded `hooks.codex.json` through the plugin manifest and put `PLUGIN_ROOT` into the hook's
   environment: the command line reads the plugin root only from there, and without it `fdg-hook.ps1` is not found
   and Codex reports a failed hook. On macOS or Linux, if available, repeat this step there for the
   `bash "$PLUGIN_ROOT/hooks/fdg-hook.sh"` line.
6. Ask "Quote the Framework Design Guidelines context you were given at session start."
   **Expected (SessionStart)**: the answer reflects this context, which the dry run printed (the model may
   paraphrase it or decline to quote hidden context, as in Task 10):

   ```
   This repository follows Framework Design Guidelines (3rd ed.).
   - Profiles: src/** = library, tests/** = test. C# 14.0; targets net10.0. Do not use newer syntax or APIs.
   - Write in .NET BCL style: Allman braces, one statement per line, ...
   - Layout and machine-checkable rules are auto-fixed and checked at the end of each turn; ...
   - Before designing or changing types, members, exception handling or common patterns, read the fdg-write skill. ...
   ```

   The C# version and target framework are those of the SDK's templates (the dry run used SDK 10.0.403).
7. Send `$fdg-setup`.
   **Expected (skill mention)**: note whether the setup skill starts. If it does not, send
   `framework-design-guidelines:fdg-setup` and note whether that starts it. When the skill lists the files it will
   change (it should keep `src/` and `tests/` and propose a profile for `tools/`), answer **no**, and check with
   `git status` that nothing changed.
8. Run `/compact` (or keep the session going until Codex compacts on its own), then ask "Which profiles does the
   FDG session context list?".
   **Expected (compaction)**: a SessionStart hook runs after the compaction, and the answer names
   `src/** = library` and `tests/** = test`. Note what the TUI shows.
9. Quit Codex. Optionally switch to the official path with
   `codex plugin marketplace add tianvan/framework-design-guidelines` in place of the clone, and note whether the
   hooks need approval again.

**Part B: Claude Code (about 10 minutes)**

1. Install from the GitHub marketplace:

   ```powershell
   claude plugin marketplace add tianvan/framework-design-guidelines
   claude plugin install framework-design-guidelines@framework-design-guidelines
   ```

2. Start `claude` in the temporary repository and run `/framework-design-guidelines:fdg-setup`.
   **Expected (interactive confirmation)**: it keeps `src/` (`library`) and `tests/` (`test`) as they are, proposes
   `application` for `tools/`, lists every file it will create or change, and waits. Ask for one change (for
   example to enable PublicApiAnalyzers, if it asks about it) and check that it lists the files again. Switch any
   such change back before you confirm: with PublicApiAnalyzers on, RS0016 and RS0037 would add to part C's list.
   Then confirm. If its package search finds only 0.2.0, tell it to use 1.0.0-rc.1. When it offers the three IDE
   severities for the root `.editorconfig`, accept. Afterwards `git status` shows only the changes it listed,
   nothing is committed, and `tools/Shipping.Tools/obj/fdg/marker.json` exists.
3. Commit the setup yourself: `git add -A`, then `git commit -m "Set up FDG"`.
4. Ask: "Add a public class OrderBatch2 that derives from Collection<Order> to src/Shipping, with a method that
   parses an int inside a try/catch written on one line."
   **Expected (Stop in a real session)**: Stop expands the catch clause, then blocks once with CA1710 and the
   quoted FDG3058 rule; the agent renames the class and the turn ends. The seeded `Order`, `OrderBatch` and
   `Quantity` are not reported, since the turn did not change them.

**Part C: IDEs (about 10 minutes)**

1. Open `Shipping.slnx` in Visual Studio, wait for the analysis to finish, and show "Build + IntelliSense" in the
   Error List.
   **Expected**: for the seeded files, the list given after the setup script; the class the agent added in part B
   and the `tools/` project, now onboarded, may add their own. CA1710 on `OrderBatch` shows that live analysis
   reads the analyzer option from the package's globalconfig, not only the severities; no CA1707 on the test method
   shows that the test profile applies.
2. Open the folder in VS Code with C# Dev Kit, set `dotnet.backgroundAnalysis.analyzerDiagnosticsScope` to
   `fullSolution` (or open the four seeded files), and look at the Problems view.
   **Expected**: the same results as in Visual Studio.

### Results

| Item | Date | Host and version | Pass or fail | Observed behavior |
|---|---|---|---|---|
| CI: integration tests on SDK 10.0.100 | 2026-11-01 | GitHub Actions | pass (after a fix) | The first run on PR #2 failed with NETSDK1004: the job restored only the test project, and the package project builds the analyzer projects through the MSBuild task. A `dotnet restore` step fixed it; 27 of 27 passed on SDK 10.0.100, on the PR, on `main` and in the `v1.0.0-rc.1` release run. |
| CI: no Node.js 20 deprecation warning | 2026-11-01 | GitHub Actions | pass | No Node.js 20 annotation on any job. The only annotation was the notice that `ubuntu-latest` moves to Ubuntu 26. |
| Codex: re-approval after the update from 0.2.0 (A4) | | | | |
| Codex: plugin hooks run, `PLUGIN_ROOT` set (A5) | | | | |
| Codex: SessionStart context (A6) | | | | |
| Codex: `$fdg-setup` mention form (A7) | | | | |
| Codex: SessionStart after compaction (A8) | | | | |
| Claude Code: `/fdg-setup` interactive confirmation (B2) | | | | |
| Claude Code: Stop blocks, the agent fixes it (B4) | | | | |
| Claude Code: Stop block limit (headless) | 2026-11-01 | Claude Code 2.1.314 | pass | With prompt 2 the agent added `[SuppressMessage]` for CA1710 after the first block ("the name was explicitly requested"), so the next Stop was silent and the limit was not reached. With "and do not suppress any diagnostic" added: two blocks with CA1710 and FDG3058, then the `Not blocking again this turn (FDG_HOOK_MAX_BLOCKS=2)` message, and the turn ended. With prompt 1 the first Stop blocked with CA1710 and FDG3058 as expected, but the agent also suppressed CA1710 instead of renaming, and asked whether to rename. |
| Codex: Stop blocks, the agent fixes it (headless) | 2026-11-01 | Codex CLI 0.164.0 | pass for the hook; agent suppressed | `codex exec` with the `-c` hooks of `hooks.codex.json` and `PLUGIN_ROOT` set to a clone at `v1.0.0-rc.1`. With prompt 1 the agent saw CA1710 in its own build and added a justified `[SuppressMessage]` before finishing, so Stop had nothing to report. With prompt 2 Stop blocked once (`blocks.txt` = 1) and the agent then suppressed CA1710. `codex exec --json` does not show hook output, so blocks were read from the hook state. |
| Codex: Stop block limit (headless) | 2026-11-01 | Codex CLI 0.164.0 | pass | With "and do not suppress any diagnostic" added to prompt 2: two blocks (`blocks.txt` = 2), three final answers, and the turn ended. |
| Visual Studio: globalconfig in live analysis (C1) | | | | |
| VS Code with C# Dev Kit: globalconfig in live analysis (C2) | | | | |
| Rider | | not installed | not verified | |
