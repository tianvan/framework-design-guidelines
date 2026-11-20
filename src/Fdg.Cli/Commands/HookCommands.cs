using Fdg.Cli.Hooks;
using Fdg.Cli.Integration;
using Fdg.Cli.Projects;

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

    public static Task<int> RunAsync(
        string hookEvent,
        TextReader input,
        TextWriter output,
        TextWriter error,
        HookRunner runner,
        CancellationToken cancellationToken)
        => RunAsync(hookEvent, input, output, error, runner, cancellationToken, host: null);

    internal static async Task<int> RunAsync(
        string hookEvent,
        TextReader input,
        TextWriter output,
        TextWriter error,
        HookRunner runner,
        CancellationToken cancellationToken,
        AgentHost? host)
    {
        string result;
        try
        {
            // Inside the outer try: a failed read of stdin is an error like any other.
            string text = await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            HookInput hookInput;
            try
            {
                hookInput = HookInput.Parse(text, Environment.GetEnvironmentVariable, host?.Kind);
            }
            catch (FormatException e)
            {
                error.WriteLine("FDG hook error: " + e.Message);
                return 1;
            }

            if (host is not null)
            {
                GitRepository? git = await GitRepository.OpenAsync(Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
                GitRepository? payload = await GitRepository.OpenAsync(hookInput.Cwd, cancellationToken).ConfigureAwait(false);
                if (git is null || payload is null || !ProjectFiles.PathComparer.Equals(git.TopLevel, payload.TopLevel) ||
                    File.Exists(Path.Combine(git.GitDirectory, "fdg-install", "transaction.json")))
                {
                    return 0;
                }

                InstallReceipt? receipt = InstallReceipt.Read(host, InstallChanges.ReadCurrent(git.TopLevel, host.ReceiptPath));
                if (receipt is null)
                {
                    return 0;
                }

                if (!string.Equals(receipt.ToolVersion, FdgCli.Version, StringComparison.OrdinalIgnoreCase))
                {
                    output.Write(HookOutput.SystemMessage($"FDG expected {receipt.ToolVersion}, but ran {FdgCli.Version}. Run: {host.PrepareCommand}"));
                    return 0;
                }
            }

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
