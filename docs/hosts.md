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
  string before running it (`codex-rs/hooks/src/engine/discovery.rs`). So `"${PLUGIN_ROOT}/hooks/fdg-hook.ps1"`
  becomes a literal path, also under PowerShell, where `${PLUGIN_ROOT}` alone would be an undefined variable.
- **`commandWindows` replaces `command` on Windows** (same file: `command_windows.unwrap_or(command)` under
  `cfg!(windows)`; the field is `commandWindows`, alias `command_windows`, in `codex-rs/config/src/hook_config.rs`).

Claude Code reads only `.claude-plugin/plugin.json` and `hooks/hooks.json`, so it never sees `hooks.codex.json`.
Option (a) of the Task 10 ruling therefore applies: `.codex-plugin/plugin.json` sets
`"hooks": "./hooks/hooks.codex.json"`, whose entries have `command` set to
`bash "${PLUGIN_ROOT}/hooks/fdg-hook.sh" <event>` and `commandWindows` set to
`pwsh -NoProfile -ExecutionPolicy Bypass -File "${PLUGIN_ROOT}/hooks/fdg-hook.ps1" <event>`. Option (b),
`commandWindows` in the shared `hooks.json`, would also pass `claude plugin validate --strict`; it was not needed.

`pwsh -File` starts its own PowerShell, so the command works whether the session shell is PowerShell or the
`cmd.exe` fallback, and `-ExecutionPolicy Bypass` keeps a machine policy such as `AllSigned` from blocking the
unsigned script. It needs PowerShell 7 (`pwsh`) on the PATH.

### The wrappers

Both wrappers run the tool's own executable, `dotnet-fdg`, not `dotnet fdg`: the `dotnet` muxer first resolves an
SDK from the `global.json` of the current directory, so `dotnet fdg` fails where that file pins an SDK that is not
installed (measured by the Task 10 review: exit 155), and it costs about 0.3 s against 0.07 s per call. They look
for `dotnet-fdg` on the PATH, then in `~/.dotnet/tools` (`%USERPROFILE%\.dotnet\tools` on Windows), which covers a
host whose PATH predates the first `dotnet tool install -g`.

- Neither found: they print
  `{"systemMessage":"FDG: dotnet-fdg is not installed. Run: dotnet tool install -g dotnet-fdg"}` and exit 0.
- Found, but `dotnet-fdg --version` fails: they print
  `{"systemMessage":"FDG: dotnet-fdg failed to start: <first line of its stderr>"}` and exit 0.
- Otherwise they run `dotnet-fdg hook <event>` with the hook payload as its stdin, byte for byte, and return its
  exit code; its stdout reaches the host unchanged. `fdg-hook.sh` `exec`s the tool. `fdg-hook.ps1` cannot pipe the
  payload through PowerShell, which would re-encode it and append a line break, so it copies the raw stdin stream
  to the child.

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

- **Codex hooks from the plugin**: that Codex loads `hooks.codex.json` through the `hooks` field of
  `.codex-plugin/plugin.json`, and that it substitutes `${PLUGIN_ROOT}` in those commands. Both are known from the
  source only, since `-c` hooks are not plugin hooks.
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
