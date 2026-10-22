namespace Fdg.Cli.Projects;

/// <summary>What the analyzer package recorded, or MSBuild reports, about a project.</summary>
/// <param name="ProjectPath">Full path of the .csproj.</param>
/// <param name="Profile">Lower-case profile, or <see langword="null"/> when not set.</param>
/// <param name="LangVersion">Concrete C# version such as <c>7.3</c> or <c>14.0</c>; symbolic values are resolved.</param>
/// <param name="TargetFramework">The project's target framework, or the first of its target frameworks.</param>
/// <param name="RulesPath">The referenced package's <c>data/rules.json</c>, when known.</param>
public sealed record ProjectInfo(
    string ProjectPath,
    string? Profile,
    string? PackageVersion,
    int? Protocol,
    string? LangVersion,
    string? TargetFramework,
    string? RulesPath,
    ProjectState State);
