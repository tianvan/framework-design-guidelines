# Host hook behavior

What `dotnet fdg hook ...` relies on in the two hosts, measured on Windows 11 with Claude Code 2.1.303 and
Codex CLI 0.163.0 (2026-10-22), plus what the Codex rust-v0.163.0 source says where a measurement was not
possible. Task 10 re-checks this with the real plugin installed.

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

## Still to confirm (Task 10)

- Whether SessionStart runs again after compaction in each host and whether its `additionalContext` is used then
  (Claude documents `source: "compact"`; the Codex schema lists `compact`).
- Behavior with the real plugin installed through each host's plugin mechanism, rather than `--plugin-dir` and `-c`.
