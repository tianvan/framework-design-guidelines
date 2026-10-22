using System.Text.Json;

namespace Fdg.Cli.Hooks;

/// <summary>One result of a SARIF log.</summary>
/// <param name="Level"><c>error</c>, <c>warning</c>, <c>note</c> or <c>none</c>.</param>
/// <param name="FilePath">Full local path, percent-decoded.</param>
public sealed record SarifDiagnostic(string RuleId, string Level, string Message, string FilePath, int Line, bool Suppressed);

/// <summary>Reads the SARIF 2.1 logs Roslyn writes for <c>ErrorLog=...,version=2.1</c>.</summary>
public static class SarifReader
{
    /// <param name="baseDirectory">Resolves relative URIs; Roslyn writes absolute <c>file:</c> URIs.</param>
    /// <exception cref="JsonException">The file is not a SARIF log.</exception>
    public static IReadOnlyList<SarifDiagnostic> Read(string path, string baseDirectory)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        var diagnostics = new List<SarifDiagnostic>();
        if (!document.RootElement.TryGetProperty("runs", out JsonElement runs) || runs.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Not a SARIF log: " + path);
        }

        foreach (JsonElement run in runs.EnumerateArray())
        {
            if (!run.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement result in results.EnumerateArray())
            {
                if (Parse(result, baseDirectory) is SarifDiagnostic diagnostic)
                {
                    diagnostics.Add(diagnostic);
                }
            }
        }

        return diagnostics;
    }

    /// <returns><see langword="null"/> for a result without a source location.</returns>
    private static SarifDiagnostic? Parse(JsonElement result, string baseDirectory)
    {
        string ruleId = String(result, "ruleId") ?? string.Empty;
        // SARIF's default level is warning.
        string level = String(result, "level") ?? "warning";
        string message = result.TryGetProperty("message", out JsonElement messageElement)
            ? String(messageElement, "text") ?? string.Empty
            : string.Empty;
        bool suppressed = result.TryGetProperty("suppressions", out JsonElement suppressions) &&
            suppressions.ValueKind == JsonValueKind.Array &&
            suppressions.GetArrayLength() > 0;
        if (!result.TryGetProperty("locations", out JsonElement locations) ||
            locations.ValueKind != JsonValueKind.Array ||
            locations.GetArrayLength() == 0 ||
            !locations[0].TryGetProperty("physicalLocation", out JsonElement physical) ||
            !physical.TryGetProperty("artifactLocation", out JsonElement artifact) ||
            String(artifact, "uri") is not string uri)
        {
            return null;
        }

        int line = physical.TryGetProperty("region", out JsonElement region) &&
            region.TryGetProperty("startLine", out JsonElement startLine) &&
            startLine.TryGetInt32(out int value)
            ? value
            : 0;
        return new SarifDiagnostic(ruleId, level, message, ToLocalPath(uri, baseDirectory), line, suppressed);
    }

    /// <summary>
    /// <c>file:///D:/a%20b/%E4%B8%AD.cs</c> becomes <c>D:\a b\中.cs</c>: <see cref="Uri.LocalPath"/> decodes
    /// percent-encoded UTF-8.
    /// </summary>
    public static string ToLocalPath(string uri, string baseDirectory)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? absolute) && absolute.IsFile)
        {
            return Path.GetFullPath(absolute.LocalPath);
        }

        return Path.GetFullPath(Uri.UnescapeDataString(uri), baseDirectory);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
