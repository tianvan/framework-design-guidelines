using System.Reflection;

namespace Fdg.Cli.Tests;

public sealed class FdgCliTests
{
    [Fact]
    public async Task Version_prints_informational_version_without_commit()
    {
        var output = new StringWriter();

        int exitCode = await FdgCli.RunAsync(["--version"], output, new StringWriter(), CancellationToken.None);

        Assert.Equal(0, exitCode);
        string informational = typeof(FdgCli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(informational.Split('+')[0] + Environment.NewLine, output.ToString());
        Assert.DoesNotContain("+", output.ToString());
    }

    [Theory]
    [InlineData]
    [InlineData("frobnicate")]
    [InlineData("fix", "--severity")]
    public async Task Invalid_arguments_print_usage_and_return_2(params string[] args)
    {
        var error = new StringWriter();

        int exitCode = await FdgCli.RunAsync(args, new StringWriter(), error, CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Contains("dotnet fdg fix [<path>...]", error.ToString());
    }
}
