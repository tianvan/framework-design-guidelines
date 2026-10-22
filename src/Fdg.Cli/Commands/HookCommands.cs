using Fdg.Cli.Hooks;

namespace Fdg.Cli.Commands;

/// <summary>
/// <c>dotnet fdg hook session-start|turn-start|turn-stop</c>: reads the host's JSON from stdin and writes the
/// hook's JSON (or nothing) to stdout. The exit code is 0 except for unparsable input (1), which both hosts treat
/// as a non-blocking hook error (docs/hosts.md). Any other failure becomes a one-line system message, so a bug in
/// the hook never blocks or breaks the AI's turn.
/// </summary>
public static class HookCommands
{
    public static IReadOnlyList<string> Events { get; } = ["session-start", "turn-start", "turn-stop"];

    public static async Task<int> RunAsync(
        string hookEvent,
        TextReader input,
        TextWriter output,
        TextWriter error,
        HookRunner runner,
        CancellationToken cancellationToken)
    {
        HookInput hookInput;
        try
        {
            hookInput = HookInput.Parse(await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (FormatException e)
        {
            error.WriteLine("FDG hook error: " + e.Message);
            return 1;
        }

        string result;
        try
        {
            result = hookEvent switch
            {
                "session-start" => await runner.SessionStartAsync(hookInput, cancellationToken).ConfigureAwait(false),
                "turn-start" => await runner.TurnStartAsync(hookInput, cancellationToken).ConfigureAwait(false),
                "turn-stop" => await runner.TurnStopAsync(hookInput, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException("unknown hook event: " + hookEvent, nameof(hookEvent)),
            };
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = HookOutput.SystemMessage("FDG hook error: " + OneLine(e.Message));
        }

        output.Write(result);
        return 0;
    }

    private static string OneLine(string message) =>
        message.ReplaceLineEndings(" ").Trim();
}
