namespace Fdg.Generator.Rules;

public enum Strength
{
    Do,
    DoNot,
    Avoid,
    Consider,

    // Appendix D gives no guidelines but a verdict on each kind of change, per its own legend: changes the .NET BCL
    // team would generally permit (check icon), would not permit (cross icon), and very contextual ones (question).
    Permitted,
    NotPermitted,
    Contextual,
}

public static class StrengthExtensions
{
    public static string ToText(this Strength strength)
    {
        return strength switch
        {
            Strength.Do => "DO",
            Strength.DoNot => "DO NOT",
            Strength.Avoid => "AVOID",
            Strength.Consider => "CONSIDER",
            Strength.Permitted => "PERMITTED",
            Strength.NotPermitted => "NOT PERMITTED",
            Strength.Contextual => "CONTEXTUAL",
            _ => throw new ArgumentOutOfRangeException(nameof(strength), strength, null),
        };
    }

    public static bool TryParse(string? text, out Strength strength)
    {
        switch (text)
        {
            case "DO":
                strength = Strength.Do;
                return true;
            case "DO NOT":
                strength = Strength.DoNot;
                return true;
            case "AVOID":
                strength = Strength.Avoid;
                return true;
            case "CONSIDER":
                strength = Strength.Consider;
                return true;
            case "PERMITTED":
                strength = Strength.Permitted;
                return true;
            case "NOT PERMITTED":
                strength = Strength.NotPermitted;
                return true;
            case "CONTEXTUAL":
                strength = Strength.Contextual;
                return true;
            default:
                strength = default;
                return false;
        }
    }

    public static Strength Parse(string? text)
    {
        if (!TryParse(text, out Strength strength))
        {
            throw new FormatException(
                $"Unknown strength '{text}'. Expected DO, DO NOT, AVOID, CONSIDER, " +
                "PERMITTED, NOT PERMITTED, or CONTEXTUAL.");
        }

        return strength;
    }

    /// <summary>Whether this is one of appendix D's verdicts on a kind of change, not a guideline strength.</summary>
    public static bool IsVerdict(this Strength strength)
    {
        return strength is Strength.Permitted or Strength.NotPermitted or Strength.Contextual;
    }

    /// <summary>Whether the book marks this guideline strength with the check icon (otherwise the cross).</summary>
    public static bool UsesCheckIcon(this Strength strength)
    {
        return strength is Strength.Do or Strength.Consider;
    }
}
