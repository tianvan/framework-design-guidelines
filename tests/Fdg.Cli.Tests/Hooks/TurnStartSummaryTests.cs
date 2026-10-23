using System.Text.Json;
using Fdg.Cli.Hooks;

namespace Fdg.Cli.Tests.Hooks;

public sealed class TurnStartSummaryTests : IDisposable
{
    private const string Session = "summary-session";

    private readonly GitTestRepo _repo = new();

    public TurnStartSummaryTests()
    {
        _repo.Write(
            "src/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"FrameworkDesignGuidelines.Analyzers\" /></ItemGroup></Project>\n");
        _repo.Write("src/A.cs", "class A { }\n");
        _repo.Write("src/B.cs", "class B { }\n");
        _repo.CommitAll();
    }

    public void Dispose()
    {
        _repo.Dispose();
    }

    [Fact]
    public async Task Turn_start_summary_only_for_codex()
    {
        await WriteAutofixAsync();
        Assert.Equal(string.Empty, await TurnStartAsync(HostKind.Claude));

        await WriteAutofixAsync();
        using JsonDocument output = JsonDocument.Parse(await TurnStartAsync(HostKind.Codex));
        JsonElement hookOutput = output.RootElement.GetProperty("hookSpecificOutput");

        Assert.Equal("UserPromptSubmit", hookOutput.GetProperty("hookEventName").GetString());
        Assert.Equal(
            "FDG auto-formatted at the end of the last turn: src/A.cs (lines 12-30, 41-44); src/B.cs (lines 3-9). " +
            "Re-read those regions before editing them.",
            hookOutput.GetProperty("additionalContext").GetString());
    }

    [Fact]
    public async Task Turn_start_summary_is_cleared_after_emit()
    {
        await WriteAutofixAsync();

        Assert.NotEqual(string.Empty, await TurnStartAsync(HostKind.Codex));
        Assert.Null((await StateAsync()).ReadAutofix());
        Assert.Equal(string.Empty, await TurnStartAsync(HostKind.Codex));
    }

    private async Task WriteAutofixAsync() =>
        (await StateAsync()).WriteAutofix(new AutofixRecord(
            1,
            [
                new AutofixFile("src/A.cs", [new LineRange(12, 30), new LineRange(41, 44)]),
                new AutofixFile("src/B.cs", [new LineRange(3, 9)]),
            ]));

    private async Task<StateStore> StateAsync() =>
        StateStore.For((await GitRepository.OpenAsync(_repo.Root, CancellationToken.None))!, Session);

    private Task<string> TurnStartAsync(HostKind host)
    {
        var runner = new HookRunner(
            new FakeHookProjects(),
            new FakeHookFormatter(),
            new FakeHookBuilder(),
            new SessionContext(new FakeProcessRunner()),
            _ => null);
        return runner.TurnStartAsync(new HookInput(host, Session, _repo.Root, "UserPromptSubmit"), CancellationToken.None);
    }
}
