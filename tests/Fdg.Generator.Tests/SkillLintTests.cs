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
    private const int MaxTopRules = 60;

    private const int MaxFdgWriteBodyLines = 250;

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
    /// <c>— FDG#### &lt;STRENGTH&gt; &lt;text&gt;</c>. An appendix D verdict is followed by a colon:
    /// <c>— FDGD001 NOT PERMITTED: &lt;text&gt;</c>.
    /// </summary>
    private static readonly Regex RuleLine = new(
        @"(?:\*\*(?<id>FDG[1-9A-Z]\d{3})\*\* |— (?<id>FDG[1-9A-Z]\d{3}) " +
        @"(?=(?:DO|AVOID|CONSIDER) |(?:PERMITTED|NOT PERMITTED|CONTEXTUAL): ))(?<rest>.+)$");

    /// <summary>
    /// Any line where a rule id is followed by a strength word, in any case and after any punctuation, such as
    /// <c>`FDG3014` Do not</c>, <c>**FDG3014**: DO</c> or <c>FDGD001 not permitted</c>. Such a line must be a
    /// <see cref="RuleLine"/>.
    /// </summary>
    private static readonly Regex LooseRuleLine = new(
        @"(?<id>FDG[1-9A-Z]\d{3})\W{0,4}(?i:DO NOT|DO|AVOID|CONSIDER|NOT PERMITTED|PERMITTED|CONTEXTUAL)\b");

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

    /// <summary>A second guard besides the generator: the checked-in references stay small enough to load.</summary>
    [Fact]
    public void Every_reference_file_is_at_most_32_KB()
    {
        string[] files = Directory.GetFiles(FdgWriteReferences, "*.md", SearchOption.AllDirectories);

        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            long bytes = new FileInfo(file).Length;
            Assert.True(
                bytes <= SkillReferenceEmitter.MaxReferenceBytes,
                $"{Relative(file)} is {bytes} bytes (max {SkillReferenceEmitter.MaxReferenceBytes}).");
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
    /// <c>**&lt;id&gt;** &lt;STRENGTH&gt; &lt;text&gt;[ (library only) | (not in test projects)][ (context: &lt;context&gt;)][ [C# N+]]</c>
    /// <c>[ (alt: &lt;alt&gt;)]</c>,
    /// with <c>(can cause: &lt;context&gt;)</c> instead of <c>(context: &lt;context&gt;)</c> for an appendix D verdict.
    /// The bracketed parts are required exactly when the rule is on in <c>library</c> and off in <c>application</c>
    /// (or on in <c>application</c> and off in <c>test</c>), has a context, a minimum language version or an
    /// alternative, so the AI always sees the profile, the version
    /// tag and the fallback. The review output form
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
                foreach (Match loose in LooseRuleLine.Matches(line))
                {
                    Assert.True(
                        match.Success && match.Groups["id"].Value == loose.Groups["id"].Value,
                        $"{Relative(file)}: the {loose.Groups["id"].Value} line is not in the rule line format:\n{line}");
                }

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

    [Theory]
    [InlineData("— FDGD001 NOT PERMITTED: Changing the Name of an Assembly")]
    [InlineData("— FDGD002 PERMITTED: Adding a Type")]
    [InlineData("— FDGD003 CONTEXTUAL: Removing a Field")]
    public void Review_output_lines_with_an_appendix_d_verdict_are_rule_lines(string line)
    {
        Match match = RuleLine.Match(line);

        Assert.True(match.Success);
        Assert.Equal(line[2..9], match.Groups["id"].Value);
        Assert.Equal(line[10..], match.Groups["rest"].Value);
    }

    [Theory]
    [InlineData("`FDGD001` Not permitted: changing the name", "FDGD001")]
    [InlineData("**FDGD002**: PERMITTED", "FDGD002")]
    [InlineData("FDGD003 contextual", "FDGD003")]
    public void Loose_rule_lines_include_the_appendix_d_verdicts(string line, string id)
    {
        Match match = LooseRuleLine.Match(line);

        Assert.True(match.Success);
        Assert.Equal(id, match.Groups["id"].Value);
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
    /// fdg-write's top rules are at most 60 rules, distinct and listed: each appears once and is a rule the references
    /// list (a judgment rule, or a CONSIDER rule analyzers only suggest). Their order (by importance) is not checked.
    /// </summary>
    [Fact]
    public void Fdg_write_top_rules_are_distinct_listed_rules()
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
    }

    /// <summary>
    /// fdg-write's routing table names every reference file in one of its rows, so every chapter can be reached by
    /// topic.
    /// </summary>
    [Fact]
    public void Fdg_write_routes_to_every_reference_file()
    {
        string[] rows = Read(SkillPath("fdg-write")).Lines.Where(l => l.StartsWith('|')).ToArray();

        string[] files = TopLevelReferenceFiles();
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            Assert.True(
                rows.Any(r => r.Contains("references/" + file, StringComparison.Ordinal)),
                $"No row of fdg-write's routing table names {file}.");
        }
    }

    /// <summary>
    /// The reviewer agent names every reference file, so a delegated review can read the rules of any topic.
    /// </summary>
    [Fact]
    public void Reviewer_agent_routes_to_every_reference_file()
    {
        string body = Read(AgentPath).Body;

        string[] files = TopLevelReferenceFiles();
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string path = "references/" + file;
            Assert.True(body.Contains(path, StringComparison.Ordinal), $"agents/fdg-reviewer.md does not name {path}.");
        }
    }

    /// <summary>
    /// fdg-review links every reference file by its path from fdg-review's directory, so a review in a session without
    /// subagents can read the rules of any topic.
    /// </summary>
    [Fact]
    public void Fdg_review_routes_to_every_reference_file()
    {
        string body = Read(SkillPath("fdg-review")).Body;

        string[] files = TopLevelReferenceFiles();
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string path = "../fdg-write/references/" + file;
            Assert.True(body.Contains(path, StringComparison.Ordinal), $"fdg-review/SKILL.md does not name {path}.");
        }
    }

    /// <summary>
    /// The listed library-only rules (on in <c>library</c>, off in <c>application</c>) carry
    /// <see cref="SkillReferenceEmitter.LibraryOnlyMarker"/> in the reference files, and the other listed rules that are
    /// on in <c>application</c> and off in <c>test</c> carry <see cref="SkillReferenceEmitter.NotInTestProjectsMarker"/>:
    /// the counts match the rules data.
    /// </summary>
    [Fact]
    public void Skill_references_mark_library_only_and_test_off_rules()
    {
        int libraryOnlyInReferences = Directory.GetFiles(FdgWriteReferences, "*.md")
            .SelectMany(f => File.ReadLines(f))
            .Count(l => l.Contains(" " + SkillReferenceEmitter.LibraryOnlyMarker));
        int testOffInReferences = Directory.GetFiles(FdgWriteReferences, "*.md")
            .SelectMany(f => File.ReadLines(f))
            .Count(l => l.Contains(" " + SkillReferenceEmitter.NotInTestProjectsMarker));

        Rule[] listed = Rules.Value.Values.Where(SkillReferenceEmitter.IsListed).ToArray();
        int libraryOnlyInData = listed.Count(SkillReferenceEmitter.IsLibraryOnly);
        int testOffInData = listed.Count(r => !SkillReferenceEmitter.IsLibraryOnly(r) && SkillReferenceEmitter.IsNotInTestProjects(r));

        Assert.Equal(libraryOnlyInData, libraryOnlyInReferences);
        Assert.Equal(testOffInData, testOffInReferences);
    }

    /// <summary>
    /// fdg-write triggers on every .cs file, so it must say first that its rules apply only to onboarded projects
    /// and that other code keeps its existing style.
    /// </summary>
    [Fact]
    public void Fdg_write_states_its_scope_before_the_rules()
    {
        string[] lines = Read(SkillPath("fdg-write")).Lines;
        int firstRule = Array.FindIndex(lines, l => RuleLine.IsMatch(l));
        string opening = string.Join("\n", lines.Take(firstRule));

        Assert.Contains("FrameworkDesignGuidelinesProfile", opening);
        Assert.Contains("SDK-style", opening);
        Assert.Contains("obj/fdg/marker.json", opening);
        Assert.Contains("existing style", opening);
        Assert.Contains("/fdg-setup", opening);
    }

    /// <summary>
    /// The skills and the reviewer agent tell the AI to skip both profile markers in test projects, and the review
    /// output, which quotes only the strength and rule text, leaves the new marker out like the others.
    /// </summary>
    [Fact]
    public void Skills_skip_both_profile_markers_in_test_projects()
    {
        const string Skip = "skip the rules marked \"(library only)\" or \"(not in test projects)\"";
        const string Omitted = "without `(library only)`, `(not in test projects)`,";

        Assert.Contains(Skip, Joined(SkillPath("fdg-write")));
        foreach (string path in new[] { SkillPath("fdg-review"), AgentPath })
        {
            string text = Joined(path);
            Assert.Contains(Skip, text);
            Assert.Contains(Omitted, text);
        }

        static string Joined(string path) => string.Join(" ", Read(path).Lines.Select(l => l.Trim()));
    }

    /// <summary>
    /// fdg-fix builds with the marker's <c>projectPath</c>, the exact string the package compares with
    /// <c>FdgSarifProject</c>, and never treats a project without a SARIF log as clean.
    /// </summary>
    [Fact]
    public void Fdg_fix_builds_the_marker_project_path_and_reports_missing_sarif()
    {
        string body = Read(SkillPath("fdg-fix")).Body;

        Assert.Contains("projectPath", body);
        Assert.Contains("-p:FdgSarifProject=<projectPath>", body);
        Assert.Contains("dotnet build <projectPath>", body);
        Assert.Contains("diagnostics could not be checked (no SARIF log)", body);
        Assert.Contains("system temporary directory", body);
    }

    [Fact]
    public void Command_skill_descriptions_name_codex_invocation()
    {
        foreach (string name in CommandSkillNames)
        {
            string description = Read(SkillPath(name)).Frontmatter.GetValueOrDefault("description") ?? string.Empty;
            Assert.Contains($"/{name}", description);
            Assert.Contains($"${name}", description);
        }
    }

    [Fact]
    public void Command_skills_disable_model_invocation()
    {
        foreach (string name in CommandSkillNames)
        {
            Assert.Equal("true", Read(SkillPath(name)).Frontmatter.GetValueOrDefault("disable-model-invocation"));
        }
    }

    private static string Statement(Rule rule) =>
        $"{rule.Strength.ToText()}{(rule.Strength.IsVerdict() ? ": " : " ")}{rule.Text}";

    /// <summary>
    /// Mirrors <c>SkillReferenceEmitter.Line</c> (through <c>RuleText.Statement</c> and <c>RuleText.LanguageTag</c>,
    /// which are internal) without the examples link; keep the two in step when the reference format changes.
    /// </summary>
    private static string QuotedLine(Rule rule)
    {
        string line = Statement(rule);
        ProfileSeverity severity = rule.Mapping.Severity;
        if (severity.Library != SeverityLevel.None && severity.Application == SeverityLevel.None)
        {
            line += " (library only)";
        }
        else if (severity.Application != SeverityLevel.None && severity.Test == SeverityLevel.None)
        {
            line += " (not in test projects)";
        }

        if (rule.Context is not null)
        {
            line += rule.Strength.IsVerdict() ? $" (can cause: {rule.Context})" : $" (context: {rule.Context})";
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

    /// <summary>The <c>.md</c> files directly in fdg-write's <c>references/</c>, not in <c>examples/</c>.</summary>
    private static string[] TopLevelReferenceFiles() =>
        Directory.GetFiles(FdgWriteReferences, "*.md")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

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
