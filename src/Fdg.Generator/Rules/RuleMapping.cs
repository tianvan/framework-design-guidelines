namespace Fdg.Generator.Rules;

public sealed record CheckInfo(CheckKind Kind, IReadOnlyList<string> Diagnostics, CoverageLevel Coverage);

public sealed record ProfileSeverity(SeverityLevel Library, SeverityLevel Application, SeverityLevel Test);

public sealed record AnalyzerInfo(string Message, string Category);

/// <summary>
/// Which IDE1006 violations a naming rule explains: the kinds of symbol it applies to
/// (<c>namespace</c>, <c>type</c>, <c>privateField</c>, ...) and the failures it covers (<c>casing</c>, <c>prefix</c>).
/// </summary>
public sealed record NamingTags(IReadOnlyList<string> Symbols, IReadOnlyList<string> Failures);

/// <summary>The hand- or AI-authored half of a rule: everything except the book's own words.</summary>
public sealed record RuleMapping(
    RuleStatus Status,
    bool Style,
    CheckInfo Check,
    AutofixKind Autofix,
    ProfileSeverity Severity,
    IReadOnlyList<string> Config,
    string? MinLanguage,
    string? Alternative,
    AnalyzerInfo? Analyzer,
    string? Notes,
    NamingTags? Naming = null)
{
    /// <summary>
    /// The skeleton the extractor writes: a draft judgment rule with strength-based severity. In a chapter that is
    /// off in the test profile, <c>severity.test</c> starts as <c>none</c>.
    /// </summary>
    public static RuleMapping CreateDefault(Strength strength, bool style, bool testProfileOn)
    {
        SeverityLevel level = strength == Strength.Consider ? SeverityLevel.Suggestion : SeverityLevel.Warning;
        return new RuleMapping(
            RuleStatus.Draft,
            style,
            new CheckInfo(CheckKind.Judgment, Array.Empty<string>(), CoverageLevel.None),
            AutofixKind.No,
            new ProfileSeverity(level, level, testProfileOn ? level : SeverityLevel.None),
            Array.Empty<string>(),
            MinLanguage: null,
            Alternative: null,
            Analyzer: null,
            Notes: null);
    }
}
