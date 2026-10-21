using System.Diagnostics;

namespace Fdg.Generator.Sdk;

/// <summary>Finds the directory of the SDK that <c>global.json</c> selects for a working directory.</summary>
public static class SdkLocator
{
    public static string FindSdkDirectory(string workingDirectory)
    {
        string version = RunDotnet(workingDirectory, "--version").Trim();
        foreach (string line in RunDotnet(workingDirectory, "--list-sdks").Split('\n'))
        {
            // Each line reads "10.0.402 [C:\Program Files\dotnet\sdk]".
            string entry = line.Trim();
            if (!entry.StartsWith(version + " [", StringComparison.Ordinal) || !entry.EndsWith(']'))
            {
                continue;
            }

            string root = entry[(version.Length + 2)..^1];
            return Path.Combine(root, version);
        }

        throw new InvalidOperationException(
            $"SDK {version} selected in {workingDirectory} is not listed by 'dotnet --list-sdks'.");
    }

    private static string RunDotnet(string workingDirectory, string argument)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start 'dotnet'.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet {argument}' failed in {workingDirectory} with exit code {process.ExitCode}: {error.Result.Trim()}");
        }

        return output;
    }
}
