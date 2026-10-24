using Fdg.Cli.Commands;
using Fdg.Cli.Projects;

namespace Fdg.Cli.Hooks;

/// <summary>What SessionStart found wrong with the setup, gathered without running MSBuild.</summary>
/// <param name="DotnetVersion">What <c>dotnet --version</c> printed in the repository, or <see langword="null"/> when it failed.</param>
/// <param name="NoProfile">Onboarded projects whose marker has no valid profile, relative to the repository root.</param>
/// <param name="NeverBuilt">Onboarded projects without a marker.</param>
/// <param name="NewerProtocol">Projects whose marker has a protocol newer than this CLI supports.</param>
/// <param name="LegacyOnboarded">Non-SDK projects that reference the package; other non-SDK projects are no issue.</param>
public sealed record SetupFacts(
    string? DotnetVersion,
    IReadOnlyList<string> NoProfile,
    IReadOnlyList<string> NeverBuilt,
    IReadOnlyList<string> NewerProtocol,
    IReadOnlyList<string> LegacyOnboarded,
    bool IsGitRepository);

/// <summary>
/// Turns <see cref="SetupFacts"/> into one <c>FDG setup issue:</c> line per problem the user can act on (Ruling 51).
/// Lines for what /fdg-setup cannot change (legacy projects, no git repository) do not point to it.
/// </summary>
public static class SelfCheck
{
    public const string IssuePrefix = "FDG setup issue: ";

    public const string MinimumSdkVersion = "8.0.100";

    /// <summary>With an older SDK (still 8.0.100 or later) some rules do not run; SessionStart notes it.</summary>
    public const string FullCoverageSdkVersion = "10.0.100";

    private const string RunSetup = " Run /fdg-setup to fix.";

    /// <summary>Projects named in one line; the rest are counted.</summary>
    private const int NamedProjects = 3;

    private static readonly Version MinimumSdk = Version.Parse(MinimumSdkVersion);

    private static readonly Version FullCoverageSdk = Version.Parse(FullCoverageSdkVersion);

    public static IReadOnlyList<string> Issues(SetupFacts facts)
    {
        var issues = new List<string>();
        if (facts.DotnetVersion is null)
        {
            issues.Add(IssuePrefix + $"dotnet --version failed here; FDG needs .NET SDK {MinimumSdkVersion} or later." + RunSetup);
        }
        else if (IsOlderThan(facts.DotnetVersion, MinimumSdk))
        {
            issues.Add(IssuePrefix + $".NET SDK {facts.DotnetVersion} is older than {MinimumSdkVersion}." + RunSetup);
        }
        else if (IsOlderThan(facts.DotnetVersion, FullCoverageSdk))
        {
            // Nothing to fix with /fdg-setup: the user may be unable to move to a newer SDK.
            issues.Add(IssuePrefix +
                $".NET SDK {facts.DotnetVersion} is older than {FullCoverageSdkVersion}: " +
                $"some rules need SDK {FullCoverageSdkVersion} or later.");
        }

        if (facts.NoProfile.Count > 0)
        {
            issues.Add(IssuePrefix +
                $"FrameworkDesignGuidelinesProfile is not library, application or test in {Names(facts.NoProfile)}." + RunSetup);
        }

        if (facts.NeverBuilt.Count > 0)
        {
            issues.Add(IssuePrefix +
                $"Never built with FrameworkDesignGuidelines.Analyzers (no obj/fdg/marker.json): {Names(facts.NeverBuilt)}. " +
                "Build them once (dotnet build); if a build does not create obj/fdg/marker.json, run /fdg-setup.");
        }

        if (facts.NewerProtocol.Count > 0)
        {
            issues.Add(IssuePrefix +
                $"FDG package protocol newer than this dotnet-fdg supports ({FixCommand.SupportedProtocol}): " +
                $"{Names(facts.NewerProtocol)}. Update it with: dotnet tool update -g dotnet-fdg");
        }

        if (facts.LegacyOnboarded.Count > 0)
        {
            issues.Add(IssuePrefix +
                $"Legacy (non-SDK) projects are not supported but reference {ProjectInfoProvider.PackageId}: " +
                $"{Names(facts.LegacyOnboarded)}. Move them out of the onboarding scope.");
        }

        if (!facts.IsGitRepository)
        {
            issues.Add(IssuePrefix + "Not a git repository: end-of-turn checks are disabled.");
        }

        return issues;
    }

    /// <summary>Compares the numeric part only, so <c>9.0.100-rc.1</c> counts as 9.0.100. Unreadable versions pass.</summary>
    private static bool IsOlderThan(string version, Version minimum)
    {
        string numeric = version.Trim().Split('-', '+')[0];
        return Version.TryParse(numeric, out Version? parsed) && parsed < minimum;
    }

    private static string Names(IReadOnlyList<string> projects) =>
        projects.Count <= NamedProjects
            ? string.Join(", ", projects)
            : string.Join(", ", projects.Take(NamedProjects)) + $" and {projects.Count - NamedProjects} more";
}
