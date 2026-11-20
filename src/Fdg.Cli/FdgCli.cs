using System.Reflection;
using System.Text;
using Fdg.Cli.Commands;
using Fdg.Cli.Hooks;
using Fdg.Cli.Integration;
using Fdg.Cli.IO;
using Fdg.Cli.Projects;
using Fdg.Cli.Tooling;

namespace Fdg.Cli;

/// <summary>The FDG command-line entry point.</summary>
public static class FdgCli
{
    private const string Usage =
        "Usage:\n" +
        "  dotnet-fdg init --host codex|claude|both [--dry-run | --yes]\n" +
        "  dotnet-fdg uninstall --host codex|claude|both [--dry-run | --yes]\n" +
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

        if (args.Count > 0 && args[0] is "init" or "uninstall")
        {
            return await IntegrationCommand.RunAsync(args, Console.In, output, error, cancellationToken).ConfigureAwait(false);
        }

        if (args.Count == 4 && args[0] == "cache" && args[1] == "prepare" && args[2] == "--host" &&
            AgentHost.Find(args[3]) is { } cacheHost)
        {
            return await RunProjectCommandAsync(async git =>
            {
                InstallReceipt? receipt = InstallReceipt.Read(cacheHost, InstallChanges.ReadCurrent(git.TopLevel, cacheHost.ReceiptPath));
                if (!string.Equals(receipt?.ToolVersion, Version, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandException($"Expected a {cacheHost.Name} installation pinned to FDG {Version}. Run its prepare command from the repository.");
                }

                await ToolCache.PrepareAsync(git.TopLevel, Version, cancellationToken).ConfigureAwait(false);
                output.WriteLine($"FDG {Version} is ready.");
                return 0;
            }, error, cancellationToken).ConfigureAwait(false);
        }

        if (args.Count == 2 && args[0] == "project")
        {
            return await RunProjectCommandAsync(git =>
            {
                string projectPath = Path.GetFullPath(args[1], Directory.GetCurrentDirectory());
                if (!WorktreePath.IsSafe(git.TopLevel, projectPath) || !File.Exists(projectPath) ||
                    !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandException("Give the path of a C# project inside this worktree.");
                }

                var runner = new ProcessRunner();
                var projects = new ProjectInfoProvider(runner, new CompilerLanguageVersions(runner));
                var (info, marker) = projects.Inspect(projectPath, queryIntermediatePath: true);
                output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    projectPath,
                    state = info.State.ToString(),
                    info.Profile,
                    info.PackageVersion,
                    info.Protocol,
                    info.LangVersion,
                    info.TargetFramework,
                    info.RulesPath,
                    markerFound = marker is not null,
                }, InstallReceipt.Json));
                return Task.FromResult(0);
            }, error, cancellationToken).ConfigureAwait(false);
        }

        AgentHost? hookHost = args.Count == 4 && args[2] == "--host" ? AgentHost.Find(args[3]) : null;
        if ((args.Count == 2 || hookHost is not null) && args[0] == "hook" && HookCommands.Events.Contains(args[1]))
        {
            var runner = new ProcessRunner();
            var languageVersions = new CompilerLanguageVersions(runner);
            var hooks = new HookRunner(
                new ProjectInfoProvider(runner, languageVersions),
                // The Stop hook's own timeout (FDG_HOOK_TIMEOUT_SECONDS) bounds formatting and building.
                new DotnetFormatRunner(runner, Timeout.InfiniteTimeSpan),
                new DotnetBuildRunner(runner, Timeout.InfiniteTimeSpan),
                new SessionContext(runner, languageVersions),
                Environment.GetEnvironmentVariable);
            // Hosts write UTF-8 JSON whatever the console code page is.
            using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return await HookCommands.RunAsync(args[1], input, output, error, hooks, cancellationToken, hookHost)
                .ConfigureAwait(false);
        }

        if (args.Count > 0 && args[0] == "fix")
        {
            bool worktreeOnly = args.Count > 1 && args[1] == "--worktree";
            string[] paths = args.Skip(worktreeOnly ? 2 : 1).ToArray();
            string? option = paths.FirstOrDefault(p => p.StartsWith('-'));
            if (option is null)
            {
                var runner = new ProcessRunner();
                var command = new FixCommand(
                    new ProjectInfoProvider(runner, new CompilerLanguageVersions(runner)),
                    new DotnetRestoreRunner(runner, RestoreTimeout),
                    new DotnetFormatRunner(runner, FormatTimeout),
                    output,
                    error);
                string cwd = Directory.GetCurrentDirectory();
                return worktreeOnly
                    ? await RunProjectCommandAsync(git => command.RunAsync(paths, cwd, cancellationToken, git.TopLevel), error, cancellationToken)
                        .ConfigureAwait(false)
                    : await command.RunAsync(paths, cwd, cancellationToken).ConfigureAwait(false);
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

    private static async Task<int> RunProjectCommandAsync(
        Func<GitRepository, Task<int>> command, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            GitRepository git = await GitRepository.OpenAsync(Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false)
                ?? throw new CommandException("Run the command inside the target Git worktree.");
            return await command(git).ConfigureAwait(false);
        }
        catch (Exception e) when (e is CommandException or ProjectEvaluationException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine("FDG: " + e.Message);
            return 1;
        }
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
