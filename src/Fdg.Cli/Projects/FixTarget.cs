namespace Fdg.Cli.Projects;

/// <summary>A project to fix, limited to <see cref="IncludeFiles"/> (full paths of files or directories) unless it is <see langword="null"/>.</summary>
public sealed record FixTarget(string ProjectPath, IReadOnlyList<string>? IncludeFiles);
