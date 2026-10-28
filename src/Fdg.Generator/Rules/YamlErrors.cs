using YamlDotNet.Core;

namespace Fdg.Generator.Rules;

/// <summary>Gives the errors of a YAML file the position that YamlDotNet keeps in <see cref="YamlException.Start"/>.</summary>
internal static class YamlErrors
{
    /// <summary>The position of the error as <c>(line,column)</c>, both counted from 1.</summary>
    public static string Position(YamlException exception)
    {
        return $"({exception.Start.Line},{exception.Start.Column})";
    }

    /// <summary>
    /// Reads <paramref name="path"/> and parses its text with <paramref name="parse"/>. A <see cref="YamlException"/>
    /// comes out with the message <c>&lt;path&gt;(&lt;line&gt;,&lt;column&gt;): &lt;original message&gt;</c>.
    /// </summary>
    public static T Read<T>(string path, Func<string, T> parse)
    {
        string text = File.ReadAllText(path);
        try
        {
            return parse(text);
        }
        catch (YamlException exception)
        {
            throw new YamlException($"{path}{Position(exception)}: {exception.Message}", exception);
        }
    }
}
