using System.Diagnostics;
using System.Text;

namespace Fdg.IntegrationTests;

/// <summary>Output of one <c>dotnet</c> invocation: exit code and interleaved stdout and stderr.</summary>
public sealed record DotnetResult(int ExitCode, string Output);

/// <summary>Runs the <c>dotnet</c> CLI in a clean environment, isolated from the test host's MSBuild state.</summary>
public static class Dotnet
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <param name="packagesDirectory">NuGet packages folder for the child, or <see langword="null"/> to keep the user's.</param>
    public static DotnetResult Run(string workingDirectory, string? packagesDirectory, params string[] arguments) =>
        RunProgram("dotnet", workingDirectory, packagesDirectory, arguments);

    /// <summary>Runs another executable, such as an installed tool, in the same clean environment.</summary>
    public static DotnetResult RunProgram(
        string fileName,
        string workingDirectory,
        string? packagesDirectory,
        params string[] arguments) =>
        RunProgram(fileName, workingDirectory, packagesDirectory, standardInput: null, new Dictionary<string, string>(), arguments);

    /// <param name="standardInput">Written to the process's stdin as UTF-8, then stdin is closed.</param>
    /// <param name="environment">Extra environment variables; MSBuild also reads them as properties.</param>
    public static DotnetResult RunProgram(
        string fileName,
        string workingDirectory,
        string? packagesDirectory,
        string? standardInput,
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The test host runs under MSBuild-related variables that would redirect the child's SDK resolution.
        foreach (string name in startInfo.Environment.Keys.ToList())
        {
            if (name.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("VSTEST", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("DOTNET_HOST_PATH", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.Environment.Remove(name);
            }
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (packagesDirectory is not null)
        {
            startInfo.Environment["NUGET_PACKAGES"] = packagesDirectory;
        }

        foreach ((string name, string value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        startInfo.RedirectStandardInput = standardInput is not null;
        var output = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);
        process.Start();
        if (standardInput is not null)
        {
            using (var stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
            {
                stdin.Write(standardInput);
            }
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} {string.Join(' ', arguments)} did not finish in {Timeout}.\n{output}");
        }

        process.WaitForExit();
        lock (output)
        {
            return new DotnetResult(process.ExitCode, output.ToString());
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(line);
        }
    }
}
