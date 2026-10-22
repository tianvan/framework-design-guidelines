using System.Text.RegularExpressions;
using Fdg.Generator.Emit;
using Fdg.Generator.Rules;
using YamlDotNet.Serialization;

namespace Fdg.Generator.Tests;

/// <summary>
/// Lints the hand-written plugin skills (<c>skills/*/SKILL.md</c>) and the reviewer agent
/// (<c>agents/fdg-reviewer.md</c>) against the Agent Skills conventions and the rule data in <c>rules/*.yaml</c>.
/// </summary>
public sealed class SkillLintTests
{
    private const int MaxTopRules = 40;

    private const int MaxFdgWriteBodyLines = 120;

    private static readonly string[] SkillNames = ["fdg-write", "fdg-setup", "fdg-fix", "fdg-review"];

    private static readonly string[] CommandSkillNames = ["fdg-setup", "fdg-fix", "fdg-review"];

    private static readonly string[] FdgWriteKeywords =
    [
        "C#", ".NET", "class", "interface", "struct", "enum", "member", "method", "property", "public API", "library",
        "naming", "exception", "writing or modifying C# code",
    ];

    /// <summary>Rule ids of the book's guidelines; tool diagnostics (<c>FDG0###</c>) are not rules.</summary>
    private static readonly Regex RuleId = new(@"\bFDG[1-9A-Z]\d{3}\b");

    /// <summary>
    /// A rule line: <c>**FDG####** &lt;STRENGTH&gt; &lt;text&gt;</c>, optionally a list item, or the review output form
    /// <c>— FDG#### &lt;STRENGTH&gt; &lt;text&gt;</c>.
    /// </summary>
    private static readonly Regex RuleLine = new(
        @"(?:\*\*(?<id>FDG[1-9A-Z]\d{3})\*\* |— (?<id>FDG[1-9A-Z]\d{3}) (?=(?:DO|AVOID|CONSIDER) ))(?<rest>.+)$");

    /// <summary>A path into a skill's references, as a markdown link target or in code.</summary>
    private static readonly Regex ReferencePath =
        new(@"(?<![\w/.-])(?<path>(?:\.\./[\w-]+/)?references/[^\s)\]`'""#]+)");

    /// <summary>
    /// Allowed reference paths: a file directly in the skill's own <c>references/</c>, or in fdg-write's when another
    /// skill needs the rule text. Second-level files (<c>references/examples/</c>) are linked from the references.
    /// </summary>
    private static readonly Regex OneLevelReference = new(@"^(?:\.\./fdg-write/)?references/[\w-]+\.md$");

    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    private static readonly Lazy<string> Root = new(FindRepositoryRoot);

    private static readonly Lazy<IReadOnlyDictionary<string, Rule>> Rules = new(() =>
        RuleLoader.LoadAll(Path.Combine(Root.Value, "rules"))
            .SelectMany(f => f.Rules)
            .ToDictionary(r => r.Id));

    private static string PluginDirectory => Path.Combine(Root.Value, "plugins", "framework-design-guidelines");

    private static string SkillPath(string name) => Path.Combine(PluginDirectory, "skills", name, "SKILL.md");

    private static string AgentPath => Path.Combine(PluginDirectory, "agents", "fdg-reviewer.md");

    private static string FdgWriteReferences => Path.Combine(PluginDirectory, "skills", "fdg-write", "references");

    /// <summary>Every SKILL.md and the reviewer agent: the files whose rule lines must quote the rule data.</summary>
    private static IEnumerable<string> LintedFiles() => SkillNames.Select(SkillPath).Append(AgentPath);

    [Fact]
    public void Every_skill_has_name_matching_directory_and_description()
    {
        string[] directories = Directory.GetDirectories(Path.Combine(PluginDirectory, "skills"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(SkillNames.Order(StringComparer.Ordinal), directories);

        foreach (string name in SkillNames)
        {
            IReadOnlyDictionary<string, string> frontmatter = Read(SkillPath(name)).Frontmatter;

            Assert.Equal(name, frontmatter.GetValueOrDefault("name"));
            string description = frontmatter.GetValueOrDefault("description") ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(description), $"{name}: description is missing.");
            Assert.True(
                description.Length <= 1024,
                $"{name}: description has {description.Length} characters (max 1024).");
        }
    }

    [Fact]
    public void Reviewer_agent_has_name_description_and_read_only_tools()
    {
        IReadOnlyDictionary<string, string> frontmatter = Read(AgentPath).Frontmatter;

        Assert.Equal("fdg-reviewer", frontmatter.GetValueOrDefault("name"));
        Assert.False(string.IsNullOrWhiteSpace(frontmatter.GetValueOrDefault("description")));
        Assert.Equal("Read, Grep, Glob, Bash", frontmatter.GetValueOrDefault("tools"));
    }

    [Fact]
    public void Skill_md_is_at_most_500_lines()
    {
        foreach (string name in SkillNames)
        {
            int lines = Read(SkillPath(name)).Lines.Length;
            Assert.True(lines <= 500, $"{name}/SKILL.md has {lines} lines (max 500).");
        }

        // fdg-write's body is always in context while the AI writes C#, and compaction keeps only its beginning.
        int bodyLines = Read(SkillPath("fdg-write")).Body.Split('\n').Length;
        Assert.True(
            bodyLines <= MaxFdgWriteBodyLines,
            $"fdg-write body has {bodyLines} lines (max {MaxFdgWriteBodyLines}).");
    }

    [Fact]
    public void References_are_linked_one_level_deep_and_exist()
    {
        foreach (string name in SkillNames)
        {
            string skillDirectory = Path.GetDirectoryName(SkillPath(name))!;
            foreach (string path in ReferencePaths(Read(SkillPath(name)).Body))
            {
                Assert.Matches(OneLevelReference, path);
                Assert.True(
                    File.Exists(Path.Combine(skillDirectory, path)),
                    $"{name}/SKILL.md links {path}, which does not exist.");
            }
        }

        string[] linked = ReferencePaths(Read(SkillPath("fdg-write")).Body).ToArray();
        foreach (string file in Directory.GetFiles(FdgWriteReferences, "*.md"))
        {
            string path = "references/" + Path.GetFileName(file);
            Assert.True(linked.Contains(path), $"fdg-write/SKILL.md does not say when to read {path}.");
        }

        // The agent is not a skill: it is told where fdg-write's references are, and names them by file.
        string[] agentPaths = ReferencePaths(Read(AgentPath).Body).ToArray();
        Assert.NotEmpty(agentPaths);
        foreach (string path in agentPaths)
        {
            Assert.Matches(OneLevelReference, path);
            Assert.True(
                File.Exists(Path.Combine(FdgWriteReferences, Path.GetFileName(path))),
                $"fdg-reviewer.md names {path}, which is not in fdg-write/references.");
        }
    }

    [Fact]
    public void Every_rule_id_mentioned_in_skills_exists_in_rules()
    {
        foreach (string file in LintedFiles())
        {
            foreach (Match match in RuleId.Matches(File.ReadAllText(file)))
            {
                Assert.True(
                    Rules.Value.ContainsKey(match.Value),
                    $"{Relative(file)} mentions {match.Value}, which is not a rule.");
            }
        }
    }

    /// <summary>
    /// The format of a rule line, the same as the generated references without their examples link:
    /// <c>**&lt;id&gt;** &lt;STRENGTH&gt; &lt;text&gt;[ (context: &lt;context&gt;)][ [C# N+]][ (alt: &lt;alt&gt;)]</c>.
    /// The bracketed parts are required exactly when the rule has a context, a minimum language version or an
    /// alternative, so the AI always sees the version tag and the fallback. The review output form
    /// <c>— &lt;id&gt; &lt;STRENGTH&gt; &lt;text&gt;</c> carries only the statement.
    /// </summary>
    [Fact]
    public void Rule_lines_quote_rule_text_verbatim()
    {
        int checkedLines = 0;
        foreach (string file in LintedFiles())
        {
            foreach (string line in Read(file).Lines)
            {
                Match match = RuleLine.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                string id = match.Groups["id"].Value;
                Assert.True(Rules.Value.TryGetValue(id, out Rule? rule), $"{Relative(file)}: {id} is not a rule.");
                bool isOutputExample = match.Value.StartsWith('—');
                string expected = isOutputExample ? Statement(rule!) : QuotedLine(rule!);
                Assert.True(
                    match.Groups["rest"].Value == expected,
                    $"{Relative(file)}: the {id} line does not quote rules/*.yaml.\n" +
                    $"Expected: {expected}\nActual:   {match.Groups["rest"].Value}");
                checkedLines++;
            }
        }

        Assert.True(checkedLines > 0, "No rule lines were found.");
    }

    [Fact]
    public void Fdg_write_has_paths_cs_glob()
    {
        IReadOnlyDictionary<string, string> frontmatter = Read(SkillPath("fdg-write")).Frontmatter;

        Assert.Equal("**/*.cs", frontmatter.GetValueOrDefault("paths"));
        Assert.NotEqual("true", frontmatter.GetValueOrDefault("disable-model-invocation"));
    }

    [Fact]
    public void Fdg_write_description_covers_trigger_keywords()
    {
        string description = Read(SkillPath("fdg-write")).Frontmatter.GetValueOrDefault("description") ?? string.Empty;

        foreach (string keyword in FdgWriteKeywords)
        {
            Assert.True(
                description.Contains(keyword, StringComparison.Ordinal),
                $"fdg-write description lacks \"{keyword}\".");
        }
    }

    /// <summary>
    /// The top rules are at most 40 rules the references list (judgment rules, or CONSIDER rules analyzers only
    /// suggest), ordered naming (chapter 3) first, then appendix A section A.1.6, then the rest of appendix A.
    /// </summary>
    [Fact]
    public void Fdg_write_top_rules_are_listed_rules_naming_first()
    {
        string[] ids = Read(SkillPath("fdg-write")).Lines
            .Select(l => RuleLine.Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["id"].Value)
            .ToArray();

        Assert.InRange(ids.Length, 1, MaxTopRules);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (string id in ids)
        {
            Assert.True(
                SkillReferenceEmitter.IsListed(Rules.Value[id]),
                $"{id} is checked by tools; it does not belong in fdg-write.");
        }

        int[] groups = ids.Select(id => Group(Rules.Value[id])).ToArray();
        Assert.Equal(groups.Order(), groups);
    }

    [Fact]
    public void Command_skills_disable_model_invocation()
    {
        foreach (string name in CommandSkillNames)
        {
            Assert.Equal("true", Read(SkillPath(name)).Frontmatter.GetValueOrDefault("disable-model-invocation"));
        }
    }

    private static int Group(Rule rule)
    {
        if (!rule.Id.StartsWith("FDGA", StringComparison.Ordinal))
        {
            return 0;
        }

        return rule.Section.StartsWith("A.1.6 ", StringComparison.Ordinal) ? 1 : 2;
    }

    private static string Statement(Rule rule) => $"{rule.Strength.ToText()} {rule.Text}";

    private static string QuotedLine(Rule rule)
    {
        string line = Statement(rule);
        if (rule.Context is not null)
        {
            line += $" (context: {rule.Context})";
        }

        string? version = rule.Mapping.MinLanguage;
        if (version is not null)
        {
            line += $" [C# {(version.EndsWith(".0", StringComparison.Ordinal) ? version[..^2] : version)}+]";
        }

        if (rule.Mapping.Alternative is not null)
        {
            line += $" (alt: {rule.Mapping.Alternative})";
        }

        return line;
    }

    private static IEnumerable<string> ReferencePaths(string text) =>
        ReferencePath.Matches(text).Select(m => m.Groups["path"].Value).Distinct();

    private static string Relative(string path) => Path.GetRelativePath(Root.Value, path).Replace('\\', '/');

    private static MarkdownFile Read(string path)
    {
        Assert.True(File.Exists(path), $"{path} does not exist.");
        string text = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.True(text.StartsWith("---\n", StringComparison.Ordinal), $"{path} has no frontmatter.");
        int end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        Assert.True(end > 0, $"{path} has an unterminated frontmatter.");

        Dictionary<string, object?> values = Yaml.Deserialize<Dictionary<string, object?>>(text[4..end]) ?? new();
        var frontmatter = values.ToDictionary(p => p.Key, p => p.Value?.ToString() ?? string.Empty);
        string body = text[(end + 5)..].TrimEnd('\n');
        return new MarkdownFile(frontmatter, body, text.TrimEnd('\n').Split('\n'));
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

    private sealed record MarkdownFile(IReadOnlyDictionary<string, string> Frontmatter, string Body, string[] Lines);
}
