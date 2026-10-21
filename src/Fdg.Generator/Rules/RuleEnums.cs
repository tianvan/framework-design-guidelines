namespace Fdg.Generator.Rules;

public enum RuleStatus
{
    Draft,
    Verified,
}

public enum CheckKind
{
    Judgment,
    Sdk,
    Fdg,
    None,
}

public enum CoverageLevel
{
    Full,
    Partial,
    None,
}

public enum AutofixKind
{
    No,
    Yes,
    Breaking,
}

public enum SeverityLevel
{
    None,
    Silent,
    Suggestion,
    Warning,
}
