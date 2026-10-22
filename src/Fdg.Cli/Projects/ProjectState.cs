namespace Fdg.Cli.Projects;

/// <summary>Whether <c>dotnet fdg</c> works on a project, and why not.</summary>
public enum ProjectState
{
    /// <summary>SDK-style, restored, references the analyzer package and sets a known profile.</summary>
    InScope,

    NotSdkStyle,

    /// <summary>Restored without FrameworkDesignGuidelines.Analyzers.</summary>
    NoPackage,

    /// <summary><c>FrameworkDesignGuidelinesProfile</c> is empty or not library, application or test.</summary>
    NoProfile,

    /// <summary>No <c>project.assets.json</c>; the other facts are unknown until a restore.</summary>
    NotRestored,
}
