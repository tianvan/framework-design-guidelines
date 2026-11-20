# Installing Framework Design Guidelines in a repository

Use Git, .NET SDK 10.0.100 or later, and an SDK-style C# project. The project can target older frameworks. FDG's
project hooks have been checked with Codex CLI 0.169.0, the desktop app's bundled Codex 0.169.1, and Claude Code
2.1.303. On Windows, use Git for Windows for Claude Code's Bash hooks and PowerShell 7 (`pwsh`) for Codex.

## First installation

From the target repository, choose a release that includes the project installer and one host value
(`codex`, `claude`, or `both`).

```sh
dotnet tool exec dotnet-fdg@<version> -- init --host codex --dry-run
dotnet tool exec dotnet-fdg@<version> -- init --host codex
```

The first command displays the file list and a diff. The second shows the plan and asks before applying it.
In scripts, pass `--yes` to FDG after `--`. SDK 10.0.100 may also prompt before downloading the installation
tool. To accept both prompts explicitly in automation, use:

```sh
dotnet tool exec --yes dotnet-fdg@<version> -- init --host codex --yes
```

`init` prepares the selected tool from the repository's NuGet sources. For a locally built release, configure
the local feed in the test repository's NuGet.Config so the bootstrap, tool preparation and Analyzer restore all
see it. The install command requires at least one project and preserves existing host settings and hooks.

Reopen your host at the Git root and trust the project. In Codex, use `/hooks` to approve the three project
hooks. Run `$fdg-setup` in Codex or `/fdg-setup` in Claude Code to choose projects and profiles and add the
Analyzer. Review and commit the integration files and the team's MSBuild changes together.

## Files to commit

| Purpose | Codex | Claude Code |
| --- | --- | --- |
| Skills | `.agents/skills/fdg-*/` | `.claude/skills/fdg-*/` |
| Hook settings | `.codex/hooks.json` | `.claude/settings.json` |
| Launchers and installation record | `.codex/hooks/` | `.claude/hooks/` |
| Project reviewer | In-session review via `$fdg-review` | `.claude/agents/fdg-reviewer.md` |

The rule references are ordinary Markdown files in one host's `fdg-write/references/`. Both hosts can read the
same copy. The installation record pins the CLI version and identifies the files owned by FDG. Setting up the
Analyzer uses the project's existing PackageReference or central package management. FDG doesn't create a tool
manifest, a `.config/` directory, or a separate top-level configuration file.

## After cloning

Trust the repository in your host and approve its hooks where required. SessionStart displays a preparation
command if the pinned executable is missing:

| Host | Command from the Git root |
| --- | --- |
| Codex on macOS/Linux | `bash .codex/hooks/fdg/fdg.sh prepare` |
| Codex on Windows | `pwsh -NoProfile -File .codex/hooks/fdg/fdg.ps1 prepare` |
| Claude Code | `bash .claude/hooks/fdg/fdg.sh prepare` from Bash or Git Bash |

Run the command once per machine and FDG version. Projects on the same version share the executable cache. The cache
is normally under `$XDG_CACHE_HOME/fdg/tools` or `~/.cache/fdg/tools` on macOS/Linux and `%LOCALAPPDATA%\FDG\tools`
on Windows. For an isolated environment, set `FDG_TOOL_CACHE` to an absolute directory outside the worktree in
both the installation environment and the host. Hook approvals remain local to the host.

Hooks run a prepared copy directly and can start with the NuGet source offline. Builds or tools invoked within
the repository retain their own dependency requirements. Stop applies formatting and safe fixes to the entire
touched C# file, then reports diagnostics on changed lines and relevant build errors. It can ask the agent to
address findings up to twice in a turn.

Codex CLI 0.169.0 reads project hook declarations for a linked worktree from its primary checkout. Merge the
hook declarations into that checkout and reopen the session so Codex can discover them. The launcher then uses the
current worktree's installation record and version. A worktree without that record stays inactive. Claude Code
can show the primary checkout's skills in a linked worktree without its own skills directory. FDG skills check
for the installation record in the current worktree before acting. [Claude Code worktree behavior](https://code.claude.com/docs/en/skills#discovery-from-parent-and-nested-directories)

## Update and remove

Run the target release's `init --host <host> --dry-run` and review the diff, then apply it. An update brings all
installed hosts and their references to the target release. If a generated file was customized, FDG points out
the conflict. Commit the result in one PR, together with any Analyzer update. Revert the PR to roll back. If the
old tool was removed from the local cache, run the preparation command again.

To remove an integration, invoke the installed release's CLI. Select a single host or `both`:

```sh
dotnet tool exec dotnet-fdg@<installed-version> -- uninstall --host codex --dry-run
dotnet tool exec dotnet-fdg@<installed-version> -- uninstall --host codex
```

You can also run the project's launcher with the same `uninstall` arguments to use its prepared copy offline.
FDG removes its hook entries and generated files and reports modified files it keeps. If the removed host held the
references, the other host receives them and its links are updated. The Analyzer and its profiles continue to serve
ordinary IDE and build workflows. A shared cache entry can still serve other repositories.
