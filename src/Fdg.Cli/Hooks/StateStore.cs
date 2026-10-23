using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fdg.Cli.Projects;

namespace Fdg.Cli.Hooks;

/// <summary>
/// Per-session hook state under <c>&lt;git-dir&gt;/fdg/&lt;session_id&gt;/</c>, which git never shows as a change:
/// <c>snapshot.json</c>, <c>baseline/&lt;sha1(path)&gt;</c>, <c>ai/&lt;sha1(path)&gt;</c>,
/// <c>formatted/&lt;sha1(path)&gt;</c>, <c>ai-lines.json</c>, <c>blocks.txt</c>, <c>autofix.json</c> and
/// <c>sarif/&lt;turn&gt;/</c>. Paths are relative to the repository root with forward slashes.
/// </summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private StateStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public static StateStore For(GitRepository git, string sessionId) =>
        new(Path.Combine(git.GitDirectory, "fdg", SanitizeSessionId(sessionId)));

    /// <summary>Keeps letters, digits, <c>-</c> and <c>_</c>, so a session id can never leave the state directory.</summary>
    public static string SanitizeSessionId(string sessionId)
    {
        var builder = new StringBuilder(sessionId.Length);
        foreach (char c in sessionId)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }

        string sanitized = builder.Length > 100 ? builder.ToString(0, 100) : builder.ToString();
        return sanitized.Length == 0 ? "unknown" : sanitized;
    }

    /// <summary>
    /// Deletes the state directories of other sessions last changed before <paramref name="cutoffUtc"/>. Every turn
    /// rewrites a session's <c>snapshot.json</c>, which changes its directory. The current session's state is kept.
    /// </summary>
    public static void DeleteOtherSessions(GitRepository git, string currentSessionId, DateTime cutoffUtc)
    {
        string root = Path.Combine(git.GitDirectory, "fdg");
        if (!System.IO.Directory.Exists(root))
        {
            return;
        }

        string current = SanitizeSessionId(currentSessionId);
        foreach (string directory in System.IO.Directory.EnumerateDirectories(root))
        {
            if (ProjectFiles.PathComparer.Equals(Path.GetFileName(directory), current) ||
                System.IO.Directory.GetLastWriteTimeUtc(directory) >= cutoffUtc)
            {
                continue;
            }

            try
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // In use; the next session tries again.
            }
        }
    }

    /// <summary>The file's content at the start of the turn.</summary>
    public string BaselinePath(string relativePath) => Path.Combine(Directory, "baseline", Sha1(relativePath));

    /// <summary>The file as the AI left it, copied before the Stop hook formats it.</summary>
    public string AiVersionPath(string relativePath) => Path.Combine(Directory, "ai", Sha1(relativePath));

    /// <summary>The file as the last Stop of this turn left it, after formatting.</summary>
    public string FormattedPath(string relativePath) => Path.Combine(Directory, "formatted", Sha1(relativePath));

    /// <summary>A directory of its own per project, since the package names SARIF logs only by target framework.</summary>
    public string SarifDirectory(int turn, string projectPath) =>
        Path.Combine(Directory, "sarif", turn.ToString(CultureInfo.InvariantCulture), Sha1(projectPath)[..12]);

    /// <summary>
    /// Removes the files of the previous turn and the block count; the autofix record is kept. The snapshot goes
    /// first: if the host kills the hook before the new one is written, Stop finds none and says so, rather than
    /// using the old snapshot with its baselines gone.
    /// </summary>
    public void ResetTurn()
    {
        Delete("snapshot.json");
        foreach (string name in (string[])["baseline", "ai", "formatted", "sarif"])
        {
            string path = Path.Combine(Directory, name);
            if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, recursive: true);
            }
        }

        Delete("ai-lines.json");
        Delete("blocks.txt");
    }

    /// <summary>
    /// Per file, the lines the AI changed this turn as of the last Stop, numbered as in <see cref="FormattedPath"/>.
    /// </summary>
    public IReadOnlyDictionary<string, int[]> ReadAiLines() =>
        ReadJson<Dictionary<string, int[]>>("ai-lines.json") ?? [];

    /// <summary>
    /// Keeps the lines of the files a Stop processed, and removes the formatted copies of every other file: a file
    /// the next Stop finds changed again is then compared with its baseline, not with a copy from before a revert.
    /// </summary>
    public void WriteAiLines(IReadOnlyDictionary<string, int[]> lines)
    {
        WriteJson("ai-lines.json", lines);
        string formatted = Path.Combine(Directory, "formatted");
        if (!System.IO.Directory.Exists(formatted))
        {
            return;
        }

        var kept = lines.Keys.Select(Sha1).ToHashSet(StringComparer.Ordinal);
        foreach (string copy in System.IO.Directory.EnumerateFiles(formatted))
        {
            if (!kept.Contains(Path.GetFileName(copy)))
            {
                File.Delete(copy);
            }
        }
    }

    /// <summary>
    /// The turn's snapshot. One written before snapshots recorded their start time started when its file was last
    /// written.
    /// </summary>
    public TurnSnapshot? ReadSnapshot()
    {
        TurnSnapshot? snapshot = ReadJson<TurnSnapshot>("snapshot.json");
        return snapshot is { Started: null }
            ? snapshot with { Started = File.GetLastWriteTimeUtc(Path.Combine(Directory, "snapshot.json")) }
            : snapshot;
    }

    public void WriteSnapshot(TurnSnapshot snapshot) => WriteJson("snapshot.json", snapshot);

    /// <summary>How many times the Stop hook has blocked in this turn.</summary>
    public int ReadBlocks()
    {
        string path = Path.Combine(Directory, "blocks.txt");
        return File.Exists(path) &&
            int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int blocks)
            ? blocks
            : 0;
    }

    public void WriteBlocks(int blocks)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, "blocks.txt"), blocks.ToString(CultureInfo.InvariantCulture));
    }

    public AutofixRecord? ReadAutofix() => ReadJson<AutofixRecord>("autofix.json");

    /// <param name="record"><see langword="null"/> removes the record.</param>
    public void WriteAutofix(AutofixRecord? record)
    {
        if (record is null)
        {
            Delete("autofix.json");
        }
        else
        {
            WriteJson("autofix.json", record);
        }
    }

    public static string Sha1(string text) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private void Delete(string name)
    {
        if (System.IO.Directory.Exists(Directory))
        {
            File.Delete(Path.Combine(Directory, name));
        }
    }

    private T? ReadJson<T>(string name)
        where T : class
    {
        string path = Path.Combine(Directory, name);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteJson<T>(string name, T value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, name), JsonSerializer.Serialize(value, JsonOptions));
    }
}
