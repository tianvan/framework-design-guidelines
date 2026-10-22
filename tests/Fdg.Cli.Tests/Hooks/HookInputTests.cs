using Fdg.Cli.Hooks;

namespace Fdg.Cli.Tests.Hooks;

public sealed class HookInputTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void Parses_fixture_payloads(string host)
    {
        HostKind expected = host == "claude" ? HostKind.Claude : HostKind.Codex;
        (string File, string Event)[] payloads =
        [
            ("session-start.json", "SessionStart"),
            ("user-prompt-submit.json", "UserPromptSubmit"),
            ("stop.json", "Stop"),
            ("stop-after-block.json", "Stop"),
        ];

        foreach ((string file, string eventName) in payloads)
        {
            HookInput input = HookInput.Parse(ReadFixture(host, file), NoEnvironment);

            Assert.Equal(expected, input.Host);
            Assert.Equal(eventName, input.EventName);
            Assert.Equal(@"C:\Users\user\source\sample", input.Cwd);
            Assert.False(string.IsNullOrEmpty(input.SessionId));
            // Only Stop payloads carry stop_hook_active; elsewhere it is absent, not false.
            Assert.Equal(eventName == "Stop" ? file == "stop-after-block.json" : null, input.StopHookActive);
        }
    }

    [Fact]
    public void Codex_plugin_root_variable_decides_when_the_fields_cannot()
    {
        const string Json = """{"session_id":"s","cwd":"C:\\x","hook_event_name":"SessionStart"}""";

        Assert.Equal(HostKind.Codex, HookInput.Parse(Json, name => name == "PLUGIN_ROOT" ? @"C:\plugin" : null).Host);
        // Codex sets CLAUDE_PLUGIN_ROOT as well, so it alone means Claude only when PLUGIN_ROOT is absent.
        Assert.Equal(HostKind.Claude, HookInput.Parse(Json, name => name == "CLAUDE_PLUGIN_ROOT" ? @"C:\plugin" : null).Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"cwd":"C:\\x","hook_event_name":"Stop"}""")]
    public void Invalid_payloads_throw_format_exception(string json)
    {
        Assert.Throws<FormatException>(() => HookInput.Parse(json, NoEnvironment));
    }

    public static string ReadFixture(string host, string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "hooks", host, file));
}
