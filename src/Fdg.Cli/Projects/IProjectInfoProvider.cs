namespace Fdg.Cli.Projects;

/// <summary>Reads project facts; replaced by a fake in tests.</summary>
public interface IProjectInfoProvider
{
    /// <exception cref="ProjectEvaluationException">MSBuild could not evaluate the project.</exception>
    ProjectInfo Get(string csprojPath);
}
