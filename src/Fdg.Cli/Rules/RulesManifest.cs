using System.Text.Json;
using Fdg.Cli.Commands;

namespace Fdg.Cli.Rules;

/// <summary>
/// The rules manifest shipped as <c>data/rules.json</c> in FrameworkDesignGuidelines.Analyzers. The CLI reads the
/// copy of the package version a project references, and keeps an embedded copy for when that one is unreadable.
/// </summary>
public sealed record RulesManifest(
    int Protocol,
    string Version,
    IReadOnlyList<RuleEntry> Rules,
    IReadOnlyList<string> RegisteredDiagnostics,
    IReadOnlyList<string> BreakingFixDiagnostics)
{
    private const string EmbeddedResourceName = "Fdg.Cli.Rules.rules.json";

    /// <summary>The registered diagnostics whose fixes FDG applies: all but the breaking ones.</summary>
    public IReadOnlyList<string> FixDiagnostics =>
        RegisteredDiagnostics.Except(BreakingFixDiagnostics, StringComparer.OrdinalIgnoreCase).ToArray();

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="JsonException">The file is not a rules manifest.</exception>
    /// <exception cref="NotSupportedException">The manifest's protocol is newer than this CLI supports.</exception>
    public static RulesManifest Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Parse(stream);
    }

    /// <summary>The manifest this CLI was built with.</summary>
    public static RulesManifest LoadEmbedded()
    {
        using Stream stream = typeof(RulesManifest).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(EmbeddedResourceName + " is not embedded.");
        return Parse(stream);
    }

    /// <summary>The manifest at <paramref name="path"/>, or the embedded one when it is missing or unreadable.</summary>
    /// <exception cref="NotSupportedException">The manifest's protocol is newer than this CLI supports.</exception>
    public static RulesManifest LoadOrEmbedded(string? path)
    {
        if (path is not null)
        {
            try
            {
                return Load(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        return LoadEmbedded();
    }

    private static RulesManifest Parse(Stream stream)
    {
        RulesManifest? manifest = JsonSerializer.Deserialize<RulesManifest>(stream, Options);
        if (manifest is null || manifest.Rules is null || manifest.BreakingFixDiagnostics is null ||
            manifest.RegisteredDiagnostics is null)
        {
            throw new JsonException("Not a rules manifest.");
        }

        if (manifest.Protocol > FixCommand.SupportedProtocol)
        {
            throw new NotSupportedException(
                $"rules.json protocol {manifest.Protocol} is newer than this dotnet-fdg supports " +
                $"({FixCommand.SupportedProtocol}); update it with: dotnet tool update -g dotnet-fdg");
        }

        return manifest;
    }
}

/// <summary>One guideline: its id, strength, the book's text, and the diagnostics that check it.</summary>
public sealed record RuleEntry(
    string Id,
    string Strength,
    string Text,
    string Section,
    IReadOnlyList<string> Diagnostics,
    string Autofix,
    IReadOnlyDictionary<string, string> Severity,
    NamingTags? Naming = null);

/// <summary>
/// Which IDE1006 violations a naming rule explains: the kinds of symbol it applies to
/// (<c>namespace</c>, <c>type</c>, <c>privateField</c>, ...) and the failures it covers (<c>casing</c>, <c>prefix</c>).
/// </summary>
public sealed record NamingTags(IReadOnlyList<string> Symbols, IReadOnlyList<string> Failures);
