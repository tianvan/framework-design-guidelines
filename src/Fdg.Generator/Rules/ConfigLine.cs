using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Fdg.Generator.Rules;

/// <summary>One <c>key = value</c> option line, as written in <c>config</c> and <c>profiles.yaml</c>.</summary>
public sealed record ConfigLine(string Key, string Value)
{
    private static readonly Regex Pattern = new(
        @"^(?<key>[A-Za-z0-9_.\-]+)[ \t]*=[ \t]*(?<value>\S(.*\S)?)$",
        RegexOptions.CultureInvariant);

    public static bool TryParse(string text, [NotNullWhen(true)] out ConfigLine? line)
    {
        Match match = Pattern.Match(text);
        line = match.Success ? new ConfigLine(match.Groups["key"].Value, match.Groups["value"].Value) : null;
        return line is not null;
    }

    public override string ToString()
    {
        return $"{Key} = {Value}";
    }
}
