using System.Text.Json;

namespace Fdg.Cli.Hooks;

/// <summary>The fields of a hook's stdin JSON that the hooks use; both hosts send the same field names.</summary>
/// <param name="EventName"><c>hook_event_name</c>: SessionStart, UserPromptSubmit or Stop.</param>
public sealed record HookInput(HostKind Host, string SessionId, string Cwd, string EventName)
{
    /// <summary>
    /// <c>stop_hook_active</c>: the host is continuing because a Stop hook blocked; <see langword="null"/> when the
    /// payload does not say.
    /// </summary>
    public bool? StopHookActive { get; init; }

    public static HookInput Parse(string json) => Parse(json, Environment.GetEnvironmentVariable);

    /// <param name="environment">Looks up environment variables; consulted only when the fields cannot tell the host.</param>
    /// <exception cref="FormatException">The input is not a hook payload.</exception>
    public static HookInput Parse(string json, Func<string, string?> environment)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new FormatException("hook input is not JSON: " + e.Message, e);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("hook input is not a JSON object");
            }

            string sessionId = RequiredString(root, "session_id");
            string cwd = RequiredString(root, "cwd");
            string eventName = RequiredString(root, "hook_event_name");
            bool? stopHookActive = root.TryGetProperty("stop_hook_active", out JsonElement active) &&
                active.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? active.GetBoolean()
                : null;
            return new HookInput(DetectHost(root, environment), sessionId, cwd, eventName)
            {
                StopHookActive = stopHookActive,
            };
        }
    }

    /// <summary>
    /// Measured with Claude Code 2.1.303 and Codex 0.163.0 (docs/hosts.md): Codex sends <c>turn_id</c> on turn
    /// events and <c>model</c> with <c>permission_mode</c> on SessionStart; Claude sends <c>prompt_id</c> on turn
    /// events and neither of the two on SessionStart. Codex also sets <c>CLAUDE_PLUGIN_ROOT</c> for plugin hooks,
    /// so only its own <c>PLUGIN_ROOT</c> tells the hosts apart.
    /// </summary>
    private static HostKind DetectHost(JsonElement root, Func<string, string?> environment)
    {
        if (root.TryGetProperty("turn_id", out _))
        {
            return HostKind.Codex;
        }

        if (root.TryGetProperty("prompt_id", out _))
        {
            return HostKind.Claude;
        }

        if (root.TryGetProperty("model", out _) && root.TryGetProperty("permission_mode", out _))
        {
            return HostKind.Codex;
        }

        if (!string.IsNullOrEmpty(environment("PLUGIN_ROOT")))
        {
            return HostKind.Codex;
        }

        return HostKind.Claude;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String &&
            value.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        throw new FormatException($"hook input has no {name}");
    }
}
