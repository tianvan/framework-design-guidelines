using System.Text.RegularExpressions;

namespace Fdg.Generator.Tests;

/// <summary>Lints the GitHub Actions workflows (<c>.github/workflows/*.yml</c>).</summary>
public sealed class WorkflowLintTests
{
    /// <summary>A step or job <c>uses:</c> line, with or without the list dash; the value runs to the end of the line.</summary>
    private static readonly Regex UsesLine = new(@"^\s*(?:-\s+)?uses:\s*(?<value>.*?)\s*$");

    /// <summary>An action pinned to a full commit SHA, followed by the version the SHA belongs to.</summary>
    private static readonly Regex PinnedAction =
        new(@"^[\w.-]+/[\w.-]+(/[\w./-]+)?@[0-9a-f]{40} # v\d+(\.\d+){0,2}$");

    private static readonly Lazy<string> Root = new(FindRepositoryRoot);

    [Fact]
    public void Every_action_is_pinned_to_a_commit_sha_with_its_version()
    {
        string[] workflows = Directory.GetFiles(Path.Combine(Root.Value, ".github", "workflows"), "*.yml");
        Assert.NotEmpty(workflows);

        List<string> failures = [];
        int checkedActions = 0;
        foreach (string workflow in workflows.Order(StringComparer.Ordinal))
        {
            string[] lines = File.ReadAllLines(workflow);
            for (int i = 0; i < lines.Length; i++)
            {
                Match uses = UsesLine.Match(lines[i]);
                if (!uses.Success)
                {
                    continue;
                }

                string value = uses.Groups["value"].Value;

                // A reusable workflow in this repository is versioned with the caller.
                if (value.StartsWith("./", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedActions++;
                if (!PinnedAction.IsMatch(value))
                {
                    failures.Add($"{Path.GetFileName(workflow)}:{i + 1}: uses: {value}");
                }
            }
        }

        Assert.True(checkedActions > 0, "No action reference was found in .github/workflows.");
        Assert.True(
            failures.Count == 0,
            "Pin every action to a commit SHA, with its version in a trailing comment (owner/repo@<40 hex> # vX.Y.Z):"
            + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("global.json was not found above the test output directory.");
    }
}
