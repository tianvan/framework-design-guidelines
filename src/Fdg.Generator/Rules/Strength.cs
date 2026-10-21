namespace Fdg.Generator.Rules;

public enum Strength
{
    Do,
    DoNot,
    Avoid,
    Consider,
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
            default:
                strength = default;
                return false;
        }
    }

    public static Strength Parse(string? text)
    {
        if (!TryParse(text, out Strength strength))
        {
            throw new FormatException($"Unknown strength '{text}'. Expected DO, DO NOT, AVOID, or CONSIDER.");
        }

        return strength;
    }

    /// <summary>Whether the book marks this strength with the check icon (otherwise the cross icon).</summary>
    public static bool UsesCheckIcon(this Strength strength)
    {
        return strength is Strength.Do or Strength.Consider;
    }
}
