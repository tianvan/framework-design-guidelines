namespace Fdg.Generator.Commands;

/// <summary>The <c>--name value</c> options of one command invocation.</summary>
public sealed class CommandOptions
{
    private readonly Dictionary<string, string> _values;

    private CommandOptions(Dictionary<string, string> values)
    {
        _values = values;
    }

    public string? Get(string name)
    {
        return _values.GetValueOrDefault(name);
    }

    /// <summary>Parses <c>--name value</c> pairs; every name must be in <paramref name="allowed"/>.</summary>
    public static bool TryParse(
        IReadOnlyList<string> args,
        IReadOnlyCollection<string> allowed,
        out CommandOptions options,
        out string error)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        options = new CommandOptions(values);
        error = string.Empty;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(arg[2..]))
            {
                error = $"Unexpected argument '{arg}'. Allowed options: {string.Join(", ", allowed.Select(a => "--" + a))}.";
                return false;
            }

            if (i + 1 >= args.Count)
            {
                error = $"Option '{arg}' needs a value.";
                return false;
            }

            values[arg[2..]] = args[++i];
        }

        return true;
    }

    public bool TryRequire(string name, out string value, out string error)
    {
        value = Get(name) ?? string.Empty;
        error = value.Length == 0 ? $"Missing required option '--{name}'." : string.Empty;
        return value.Length > 0;
    }
}
