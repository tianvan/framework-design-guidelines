using System.Text.Json;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests;

/// <summary>Records invocations and answers each with the first matching canned result.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(Func<IReadOnlyList<string>, bool> Match, ToolResult Result)> _responses = [];

    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// <summary>The timeout of each call, in the order of <see cref="Calls"/>.</summary>
    public List<TimeSpan> Timeouts { get; } = [];

    public FakeProcessRunner Respond(Func<IReadOnlyList<string>, bool> match, ToolResult result)
    {
        _responses.Add((match, result));
        return this;
    }

    /// <summary>Answers <c>dotnet msbuild -getProperty</c> queries with the given properties as JSON.</summary>
    public FakeProcessRunner RespondToMSBuild(IReadOnlyDictionary<string, string> properties)
    {
        string json = JsonSerializer.Serialize(new { Properties = properties });
        return Respond(args => args.Count > 0 && args[0] == "msbuild", new ToolResult(0, json + "\n", false));
    }

    public Task<ToolResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Calls.Add(arguments);
        Timeouts.Add(timeout);
        foreach ((Func<IReadOnlyList<string>, bool> match, ToolResult result) in _responses)
        {
            if (match(arguments))
            {
                return Task.FromResult(result);
            }
        }

        return Task.FromResult(new ToolResult(1, "unexpected call: " + string.Join(' ', arguments), false));
    }
}
