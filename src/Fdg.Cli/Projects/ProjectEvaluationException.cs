namespace Fdg.Cli.Projects;

/// <summary><c>dotnet msbuild -getProperty</c> failed for a project.</summary>
public sealed class ProjectEvaluationException : Exception
{
    public ProjectEvaluationException(string message, string output)
        : base(message)
    {
        Output = output;
    }

    /// <summary>The MSBuild output explaining the failure.</summary>
    public string Output { get; }
}
