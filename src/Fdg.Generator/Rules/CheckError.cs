namespace Fdg.Generator.Rules;

/// <summary>One problem found by <c>check</c>, printed as <c>&lt;file&gt;:&lt;ruleId&gt;: &lt;message&gt;</c>.</summary>
public sealed record CheckError(string File, string RuleId, string Message)
{
    /// <summary>The rule id used for problems that do not belong to a single rule.</summary>
    public const string NoRule = "-";

    public override string ToString()
    {
        return $"{File}:{RuleId}: {Message}";
    }
}
