using System.Reflection;
using Fdg.Cli.Commands;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli;

/// <summary>Hand-written argument parsing: the CLI has only <c>fix</c>, the hook entry points and <c>--version</c>.</summary>
public static class FdgCli
{
    private const string Usage =
        "Usage:\n" +
        "  dotnet fdg fix [<path>...]   Apply formatting and non-breaking fixes. Paths: directories, .csproj,\n" +
        "                               .sln, .slnx or .cs files; the default is the current directory.\n" +
        "  dotnet fdg --version";

    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan FormatTimeout = TimeSpan.FromMinutes(30);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Count == 1 && args[0] == "--version")
        {
            output.WriteLine(Version);
            return 0;
        }

        if (args.Count == 1 && args[0] is "-h" or "--help" or "-?")
        {
            output.WriteLine(Usage);
            return 0;
        }

        if (args.Count > 0 && args[0] == "fix")
        {
            string[] paths = args.Skip(1).ToArray();
            string? option = paths.FirstOrDefault(p => p.StartsWith('-'));
            if (option is null)
            {
                var runner = new ProcessRunner();
                var command = new FixCommand(
                    new ProjectInfoProvider(runner),
                    new DotnetRestoreRunner(runner, RestoreTimeout),
                    new DotnetFormatRunner(runner, FormatTimeout),
                    output,
                    error);
                return await command.RunAsync(paths, Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
            }

            error.WriteLine($"dotnet fdg fix takes no options: {option}");
        }
        else if (args.Count > 0)
        {
            error.WriteLine($"unknown command: {args[0]}");
        }

        error.WriteLine(Usage);
        return 2;
    }

    /// <summary>The informational version without the <c>+commit</c> suffix the SDK appends.</summary>
    public static string Version
    {
        get
        {
            string version = typeof(FdgCli).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(FdgCli).Assembly.GetName().Version?.ToString()
                ?? "0.0.0";
            int plus = version.IndexOf('+');
            return plus < 0 ? version : version[..plus];
        }
    }
}
