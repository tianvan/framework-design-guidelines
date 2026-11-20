using System.Text.Json.Nodes;
using Fdg.Cli.Hooks;

namespace Fdg.Cli.Integration;

internal sealed record AgentHost(string Name, HostKind Kind, string Directory, string Skills, string Configuration)
{
    public static AgentHost Codex { get; } = new("codex", HostKind.Codex, ".codex", ".agents/skills", ".codex/hooks.json");

    public static AgentHost Claude { get; } = new("claude", HostKind.Claude, ".claude", ".claude/skills", ".claude/settings.json");

    public static IReadOnlyList<AgentHost> All { get; } = [Codex, Claude];

    public string ReceiptPath => Directory + "/hooks/fdg-install.json";

    public string LauncherDirectory => Directory + "/hooks/fdg";

    public string References => Skills + "/fdg-write/references";

    public string PrepareCommand => UsesPowerShell
        ? $"pwsh -NoProfile -File {LauncherDirectory}/fdg.ps1 prepare"
        : $"bash {LauncherDirectory}/fdg.sh prepare";

    private bool UsesPowerShell => this == Codex && OperatingSystem.IsWindows();

    public static AgentHost? Find(string? name) => All.FirstOrDefault(host => host.Name == name);

    internal static IReadOnlyDictionary<string, (string Argument, int Timeout)> Events { get; } =
        new Dictionary<string, (string, int)>(StringComparer.Ordinal)
        {
            ["SessionStart"] = ("session-start", 15),
            ["UserPromptSubmit"] = ("turn-start", 15),
            ["Stop"] = ("turn-stop", 90),
        };

    public Dictionary<string, JsonObject> HookGroups() =>
        Events.ToDictionary(entry => entry.Key, entry => Group(entry.Value.Argument, entry.Value.Timeout), StringComparer.Ordinal);

    private JsonObject Group(string hookEvent, int timeout)
    {
        string path = LauncherDirectory + "/fdg.sh";
        // Resolve from the hook's cwd even when Codex found the declaration in the main checkout.
        // Keep the command stable across versions so upgrades do not move its approval fingerprint.
        var command = new JsonObject
        {
            ["type"] = "command",
            ["command"] = "bash -c 'root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0; " +
                $"for path in \"$root/{Directory}\" \"$root/{Directory}/hooks\" \"$root/{LauncherDirectory}\" \"$root/{path}\"; do " +
                $"[ ! -L \"$path\" ] || exit 0; done; [ -f \"$root/{path}\" ] || exit 0; exec bash \"$root/{path}\" hook {hookEvent}'",
            ["timeout"] = timeout,
        };
        if (this == Codex)
        {
            // Works both from PowerShell and cmd.exe. There are no outer-shell variables in the inner command.
            command["commandWindows"] = "pwsh -NoProfile -ExecutionPolicy Bypass -Command \"" +
                "& { try { Set-Location -LiteralPath (git rev-parse --show-toplevel 2>&1) -ErrorAction Stop } catch { return }; " +
                $"if (Get-Item -LiteralPath '{Directory}','{Directory}/hooks','{LauncherDirectory}','{LauncherDirectory}/fdg.ps1' " +
                "-Force -ErrorAction SilentlyContinue | Where-Object Attributes -Match ReparsePoint) { return }; " +
                $"if (Test-Path -LiteralPath '{LauncherDirectory}/fdg.ps1' -PathType Leaf) {{ & './{LauncherDirectory}/fdg.ps1' hook {hookEvent} }} }}\"";
        }

        return new JsonObject { ["hooks"] = new JsonArray(command) };
    }
}
