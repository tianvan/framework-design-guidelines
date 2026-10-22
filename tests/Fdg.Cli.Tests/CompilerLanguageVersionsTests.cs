using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli.Tests;

public sealed class CompilerLanguageVersionsTests : IDisposable
{
    private readonly TempTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void Parses_sdk_10_0_402_compiler_output()
    {
        // Captured with: dotnet exec "C:\Program Files\dotnet\sdk\10.0.402\Roslyn\bincore\csc.dll" -langversion:?
        string output = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "csc-langversion-10.0.402.txt"));

        SupportedLanguageVersions versions = SupportedLanguageVersions.Parse(output)!;

        Assert.Equal(new SupportedLanguageVersions("14.0", "14.0", "14.0"), versions);
        Assert.Equal("14.0", versions.Resolve("latest"));
        Assert.Equal("14.0", versions.Resolve("latestMajor"));
        Assert.Equal("14.0", versions.Resolve("default"));
        Assert.Equal("14.0", versions.Resolve("preview"));
    }

    [Fact]
    public void Latest_includes_minor_versions_and_preview_includes_preview_versions()
    {
        string output = "Supported language versions:\ndefault\n1\n7.0\n7.1\n7.3 (default)\n8.0 (preview)\nlatestmajor\npreview\nlatest\n";

        SupportedLanguageVersions versions = SupportedLanguageVersions.Parse(output)!;

        Assert.Equal(new SupportedLanguageVersions("7.3", "7.0", "8.0"), versions);
    }

    [Fact]
    public void Output_without_versions_is_not_parsed()
    {
        Assert.Null(SupportedLanguageVersions.Parse("error CS2007: Unrecognized option"));
    }

    [Fact]
    public void Resolves_with_the_compiler_of_the_selected_sdk_and_caches_it()
    {
        string sdkRoot = _tree.Directory("dotnet/sdk");
        string compiler = _tree.Write("dotnet/sdk/10.0.402/Roslyn/bincore/csc.dll");
        string project = _tree.Directory("A");
        var runner = new FakeProcessRunner()
            .Respond(a => a[0] == "--version", new ToolResult(0, "10.0.402\n", false))
            .Respond(
                a => a[0] == "--list-sdks",
                new ToolResult(0, $"6.0.428 [{sdkRoot}]\n10.0.402 [{sdkRoot}]\n", false))
            .Respond(
                a => a[0] == "exec" && a[1] == compiler && a[2] == "-langversion:?",
                new ToolResult(0, "Supported language versions:\n7.3\n12.0\n12.1\n13.0 (default)\nlatest\n", false));
        var source = new CompilerLanguageVersions(runner);

        Assert.Equal("13.0", source.Resolve(project, "latest"));
        Assert.Equal("13.0", source.Resolve(project, "default"));
        Assert.Single(runner.Calls, c => c[0] == "exec");
    }

    [Fact]
    public void Installed_sdk_compiler_resolves_latest_above_the_netstandard_default()
    {
        // Runs the real dotnet: the SDK this repository's global.json selects ships C# 14.
        var source = new CompilerLanguageVersions(new ProcessRunner());

        string? latest = source.Resolve(AppContext.BaseDirectory, "latest");

        Assert.NotNull(latest);
        Assert.True(Version.Parse(latest) >= new Version(14, 0), latest);
    }

    [Fact]
    public void Unknown_sdk_gives_no_version()
    {
        var runner = new FakeProcessRunner()
            .Respond(a => a[0] == "--version", new ToolResult(0, "10.0.402\n", false))
            .Respond(a => a[0] == "--list-sdks", new ToolResult(0, "8.0.100 [C:\\dotnet\\sdk]\n", false));

        Assert.Null(new CompilerLanguageVersions(runner).Resolve(_tree.Root, "latest"));
    }
}
