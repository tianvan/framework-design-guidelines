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

    [Fact]
    public async Task Many_ranges_are_summarized_as_reformatted_throughout()
    {
        LineRange[] ranges = Enumerable.Range(0, 11).Select(i => new LineRange(i * 10 + 1, i * 10 + 2)).ToArray();
        (await StateAsync()).WriteAutofix(new AutofixRecord(1, [new AutofixFile("src/A.cs", ranges, LineCount: 1000)]));

        Assert.Equal(
            "FDG auto-formatted at the end of the last turn: src/A.cs (reformatted throughout). " +
            "Re-read those regions before editing them.",
            await SummaryAsync());
    }

    [Fact]
    public async Task Ten_ranges_are_still_listed()
    {
        LineRange[] ranges = Enumerable.Range(0, 10).Select(i => new LineRange(i * 10 + 1, i * 10 + 1)).ToArray();
        (await StateAsync()).WriteAutofix(new AutofixRecord(1, [new AutofixFile("src/A.cs", ranges, LineCount: 1000)]));

        Assert.Contains("src/A.cs (lines 1, 11, 21, 31, 41, 51, 61, 71, 81, 91)", await SummaryAsync());
    }

    [Fact]
    public async Task Half_the_file_is_summarized_as_reformatted_throughout()
    {
        (await StateAsync()).WriteAutofix(new AutofixRecord(
            1,
            [
                new AutofixFile("src/A.cs", [new LineRange(1, 6)], LineCount: 10),
                new AutofixFile("src/B.cs", [new LineRange(1, 5)], LineCount: 10),
            ]));

        Assert.Equal(
            "FDG auto-formatted at the end of the last turn: src/A.cs (reformatted throughout); src/B.cs (lines 1-5). " +
            "Re-read those regions before editing them.",
            await SummaryAsync());
    }

    [Fact]
    public async Task Record_without_line_count_reads_as_zero_and_lists_the_ranges()
    {
        StateStore state = await StateAsync();
        Directory.CreateDirectory(state.Directory);
        File.WriteAllText(
            Path.Combine(state.Directory, "autofix.json"),
            """{"turn":1,"files":[{"path":"src/A.cs","ranges":[{"start":1,"end":9}]}]}""");

        AutofixRecord record = state.ReadAutofix()!;

        Assert.Equal(0, record.Files[0].LineCount);
        Assert.Equal(
            "FDG auto-formatted at the end of the last turn: src/A.cs (lines 1-9). Re-read those regions before editing them.",
            await SummaryAsync());
    }

    [Fact]
    public void Merge_returns_current_for_another_turn_or_no_previous_record()
    {
        var current = new AutofixRecord(2, [new AutofixFile("src/A.cs", [new LineRange(1, 1)])]);

        Assert.Same(current, AutofixRecord.Merge(null, current));
        Assert.Same(current, AutofixRecord.Merge(new AutofixRecord(1, [new AutofixFile("src/B.cs", [new LineRange(1, 1)])]), current));
    }

    [Fact]
    public void Merge_unions_the_ranges_of_a_file_and_coalesces_overlapping_and_adjacent_ranges()
    {
        var previous = new AutofixRecord(
            1,
            [
                new AutofixFile("src/A.cs", [new LineRange(1, 5), new LineRange(20, 22)], LineCount: 50),
                new AutofixFile("src/B.cs", [new LineRange(3, 3)]),
            ]);
        var current = new AutofixRecord(
            1,
            [
                new AutofixFile("src/A.cs", [new LineRange(4, 8), new LineRange(9, 10), new LineRange(23, 24), new LineRange(40, 41)], LineCount: 52),
                new AutofixFile("src/C.cs", [new LineRange(7, 7)]),
            ]);

        AutofixRecord merged = AutofixRecord.Merge(previous, current);

        Assert.Equal(["src/A.cs", "src/B.cs", "src/C.cs"], merged.Files.Select(f => f.Path));
        AutofixFile a = merged.Files[0];
        Assert.Equal([new LineRange(1, 10), new LineRange(20, 24), new LineRange(40, 41)], a.Ranges);
        Assert.Equal(52, a.LineCount);
        Assert.Equal([new LineRange(3, 3)], merged.Files[1].Ranges);
    }

    private async Task<string> SummaryAsync()
    {
        using JsonDocument output = JsonDocument.Parse(await TurnStartAsync(HostKind.Codex));
        return output.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()!;
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
