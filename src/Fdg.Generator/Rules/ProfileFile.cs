using YamlDotNet.Serialization;

namespace Fdg.Generator.Rules;

/// <summary>Loads <c>profiles.yaml</c>: the base option lines of each profile.</summary>
public static class ProfileFile
{
    public const string FileName = "profiles.yaml";

    /// <summary>The profiles, in the order used by the severity fields.</summary>
    public static readonly IReadOnlyList<string> Names = new[] { "library", "application", "test" };

    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Load(string rulesDir)
    {
        string path = Path.Combine(rulesDir, FileName);
        Dictionary<string, List<string>>? profiles =
            Deserializer.Deserialize<Dictionary<string, List<string>>?>(File.ReadAllText(path));

        return (profiles ?? new Dictionary<string, List<string>>()).ToDictionary(
            p => p.Key,
            p => (IReadOnlyList<string>)(p.Value ?? new List<string>()),
            StringComparer.Ordinal);
    }
}
