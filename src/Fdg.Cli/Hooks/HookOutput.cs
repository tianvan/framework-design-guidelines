using System.Text.Json;

namespace Fdg.Cli.Hooks;

/// <summary>
/// The JSON a hook writes to stdout. Non-ASCII characters are escaped, so the output does not depend on how the
/// host decodes it.
/// </summary>
public static class HookOutput
{
    /// <summary>Nothing to say: the hook writes nothing.</summary>
    public const string None = "";

    /// <summary>Keeps the AI working: <c>{"decision":"block","reason":"..."}</c>.</summary>
    public static string Block(string reason) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["decision"] = "block", ["reason"] = reason });

    /// <summary>Lets the turn end and shows the user a message: <c>{"systemMessage":"..."}</c>.</summary>
    public static string SystemMessage(string message) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["systemMessage"] = message });

    /// <summary>Adds text to the AI's context: <c>{"hookSpecificOutput":{"hookEventName":...,"additionalContext":...}}</c>.</summary>
    /// <param name="systemMessage">Also shown to the user, as a top-level <c>systemMessage</c>, when not <see langword="null"/>.</param>
    public static string Context(string eventName, string additionalContext, string? systemMessage = null)
    {
        var output = new Dictionary<string, object>();
        if (systemMessage is not null)
        {
            output["systemMessage"] = systemMessage;
        }

        output["hookSpecificOutput"] = new Dictionary<string, string>
        {
            ["hookEventName"] = eventName,
            ["additionalContext"] = additionalContext,
        };
        return JsonSerializer.Serialize(output);
    }
}
