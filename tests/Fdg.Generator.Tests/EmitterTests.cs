using System.Text.Json.Nodes;
using Fdg.Generator.Commands;
using Fdg.Generator.Emit;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Tests;

public sealed class EmitterTests : IDisposable
{
    private readonly string _fixtureRules = Path.Combine(AppContext.BaseDirectory, "fixtures", "rules");
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (string dir in _tempDirs)
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private GeneratorInput Input()
    {
        return GeneratorInput.Load(_fixtureRules, "0.1.0");
    }

    private static GeneratorInput Edit(GeneratorInput input, Func<Rule, Rule> edit)
    {
        return input with
        {
            Files = input.Files.Select(f => f with { Rules = f.Rules.Select(edit).ToList() }).ToList(),
        };
    }

    private static Rule Map(Rule rule, Func<RuleMapping, RuleMapping> map)
    {
        return rule with { Mapping = map(rule.Mapping) };
    }

    private static Func<Rule, Rule> OnRule(string id, Func<RuleMapping, RuleMapping> map)
    {
        return rule => rule.Id == id ? Map(rule, map) : rule;
    }

    private static string[] Lines(string text)
    {
        return text.Split('\n');
    }

    private string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fdg-emit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static string ReferenceMain(GeneratorInput input)
    {
        return SkillReferenceEmitter.Emit(input).Single(f => f.Path.EndsWith("references/ch09-sample.md", StringComparison.Ordinal)).Content;
    }

    [Fact]
    public void Rules_json_protocol_matches_the_package_props()
    {
        string props = Path.Combine(
            InstalledSdkFixture.RepositoryRoot(), "src", "FrameworkDesignGuidelines.Package", "build", "FrameworkDesignGuidelines.Analyzers.props");

        // The CLI's FixCommand.SupportedProtocol is bound to the same props value in Fdg.Cli.Tests.
        Assert.Equal(
            RulesJsonEmitter.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Xml.Linq.XDocument.Load(props).Descendants("FdgProtocolVersion").Single().Value);
    }

    [Fact]
    public void Severity_for_shared_diagnostic_is_max_across_rules()
    {
        // FDG9003 (suggestion) and FDG9009 (warning in library) both map IDE1006.
        GeneratorInput input = Input();

        Assert.Contains("dotnet_diagnostic.IDE1006.severity = warning", Lines(GlobalConfigEmitter.Emit(input, "library")));
        Assert.Contains("dotnet_diagnostic.IDE1006.severity = suggestion", Lines(GlobalConfigEmitter.Emit(input, "application")));
        Assert.Contains("dotnet_diagnostic.IDE1006.severity = silent", Lines(GlobalConfigEmitter.Emit(input, "test")));
    }

    [Fact]
    public void Conflicting_config_values_fail_with_both_rule_ids()
    {
        GeneratorInput input = Edit(Input(), OnRule("FDG9010", m => m with { Config = new[] { "indent_size = 4" } }));
        input = Edit(input, OnRule("FDG9001", m => m with { Config = new[] { "indent_size = 2" } }));

        GenerationException exception = Assert.Throws<GenerationException>(() => GlobalConfigEmitter.Emit(input, "library"));

        Assert.Contains("FDG9001", exception.Message);
        Assert.Contains("FDG9010", exception.Message);
        Assert.Contains("indent_size", exception.Message);
    }

    [Fact]
    public void Equal_config_values_are_written_once()
    {
        GeneratorInput input = Edit(Input(), OnRule("FDG9001", m => m with { Config = new[] { "indent_size = 4" } }));

        string[] lines = Lines(GlobalConfigEmitter.Emit(input, "library"));

        Assert.Single(lines, "indent_size = 4");
    }

    [Fact]
    public void Code_style_option_carries_the_severity_of_its_rules_diagnostics()
    {
        // dotnet format reads an IDE rule's severity from the option suffix, not from a globalconfig's dotnet_diagnostic.
        GeneratorInput input = Edit(Input(), OnRule("FDG9010", m => m with
        {
            Check = m.Check with { Diagnostics = new[] { "IDE0011" } },
            Config = new[] { "csharp_prefer_braces = true" },
        }));

        Assert.Contains("csharp_prefer_braces = true:warning", Lines(GlobalConfigEmitter.Emit(input, "library")));
        Assert.Contains("dotnet_diagnostic.IDE0011.severity = warning", Lines(GlobalConfigEmitter.Emit(input, "library")));
    }

    [Fact]
    public void Formatting_and_suffixed_options_are_written_as_is()
    {
        GeneratorInput input = Edit(Input(), OnRule("FDG9010", m => m with
        {
            Check = m.Check with { Diagnostics = new[] { "IDE0055", "IDE0065" } },
            Config = new[] { "csharp_new_line_before_catch = true", "csharp_using_directive_placement = outside_namespace:silent" },
        }));

        string[] lines = Lines(GlobalConfigEmitter.Emit(input, "library"));

        Assert.Contains("csharp_new_line_before_catch = true", lines);
        Assert.Contains("csharp_using_directive_placement = outside_namespace:silent", lines);
    }

    [Fact]
    public void Globalconfig_starts_with_is_global_and_level()
    {
        foreach (string profile in new[] { "library", "application", "test" })
        {
            string[] lines = Lines(GlobalConfigEmitter.Emit(Input(), profile));

            Assert.Equal("is_global = true", lines[0]);
            Assert.Equal("global_level = -1", lines[1]);
        }
    }

    [Fact]
    public void Globalconfig_has_base_options_then_rule_config_then_diagnostics()
    {
        string[] lines = Lines(GlobalConfigEmitter.Emit(Input(), "library"));

        int baseOption = Array.IndexOf(lines, "dotnet_code_quality.api_surface = public");
        int ruleConfig = Array.IndexOf(lines, "indent_size = 4");
        int diagnostic = Array.IndexOf(lines, "dotnet_diagnostic.CA1000.severity = suggestion");

        Assert.InRange(baseOption, 2, int.MaxValue);
        Assert.True(baseOption < ruleConfig);
        Assert.True(ruleConfig < diagnostic);
    }

    [Fact]
    public void Tests_profile_omits_rules_with_none()
    {
        string[] test = Lines(GlobalConfigEmitter.Emit(Input(), "test"));
        string[] library = Lines(GlobalConfigEmitter.Emit(Input(), "library"));

        // The rule's options are dropped, but the diagnostic is switched off explicitly so no SDK default applies.
        Assert.Contains("dotnet_code_quality.CA1716.api_surface = public", library);
        Assert.DoesNotContain("dotnet_code_quality.CA1716.api_surface = public", test);
        Assert.Contains("dotnet_diagnostic.CA1716.severity = none", test);
        Assert.Contains("dotnet_diagnostic.CA1710.severity = none", test);
    }

    [Fact]
    public void Naming_rule_severity_follows_the_owning_rule_per_profile()
    {
        GeneratorInput input = Input();

        Assert.Contains("dotnet_naming_rule.fdg9009_public_fields.severity = warning", Lines(GlobalConfigEmitter.Emit(input, "library")));
        Assert.Contains("dotnet_naming_rule.fdg9009_public_fields.severity = suggestion", Lines(GlobalConfigEmitter.Emit(input, "application")));
        Assert.Contains("dotnet_naming_rule.fdg9009_public_fields.severity = silent", Lines(GlobalConfigEmitter.Emit(input, "test")));
    }

    [Fact]
    public void Naming_rule_severity_is_not_written_for_a_rule_that_is_none_in_the_profile()
    {
        GeneratorInput input = Edit(Input(), OnRule("FDG9009", m => m with { Severity = new ProfileSeverity(SeverityLevel.Warning, SeverityLevel.Warning, SeverityLevel.None) }));

        string test = GlobalConfigEmitter.Emit(input, "test");

        Assert.DoesNotContain("fdg9009_public_fields", test);
    }

    [Fact]
    public void Naming_rule_severity_placeholder_in_config_is_replaced_not_a_conflict()
    {
        // Two rules carry different placeholder severities for one naming rule; the per-profile value wins.
        string[] naming =
        {
            "dotnet_naming_rule.fdg9009_public_fields.symbols = fdg_public_fields",
            "dotnet_naming_rule.fdg9009_public_fields.style = fdg_pascal_case",
            "dotnet_naming_symbols.fdg_public_fields.applicable_kinds = field",
            "dotnet_naming_style.fdg_pascal_case.capitalization = pascal_case",
        };
        GeneratorInput input = Edit(Input(), OnRule("FDG9009", m => m with { Config = naming.Append("dotnet_naming_rule.fdg9009_public_fields.severity = error").ToList() }));

        string[] lines = Lines(GlobalConfigEmitter.Emit(input, "application"));

        Assert.Single(lines, l => l.StartsWith("dotnet_naming_rule.fdg9009_public_fields.severity", StringComparison.Ordinal));
        Assert.Contains("dotnet_naming_rule.fdg9009_public_fields.severity = suggestion", lines);
    }

    [Fact]
    public void Skill_reference_excludes_tool_checked_do_rules()
    {
        string main = ReferenceMain(Input());

        Assert.Contains("**FDG9001**", main);
        Assert.DoesNotContain("**FDG9002**", main);
        Assert.DoesNotContain("**FDG9003**", main);
        Assert.DoesNotContain("**FDG9007**", main);
        Assert.DoesNotContain("**FDG9009**", main);
        Assert.DoesNotContain("**FDG9010**", main);
    }

    [Fact]
    public void Skill_reference_includes_consider_rules_with_analyzers()
    {
        string main = ReferenceMain(Input());

        Assert.Contains("**FDG9006** CONSIDER making types sealed.", main);
        Assert.Contains("**FDG9008** CONSIDER writing a short summary for each type.", main);
    }

    [Fact]
    public void Skill_reference_tags_min_language()
    {
        string line = Lines(ReferenceMain(Input())).Single(l => l.Contains("**FDG9001**", StringComparison.Ordinal));

        Assert.Equal(
            "- **FDG9001** DO prefer records for immutable data carriers. [C# 9+] (alt: Use a sealed class with read-only properties.)" +
            " — examples: examples/ch09-sample.md#fdg9001",
            line);
    }

    /// <summary>The sample chapter with the given reference splits; <paramref name="keep"/> restricts it to some rules.</summary>
    private GeneratorInput Split(Func<Rule, bool> keep, params ReferenceSplit[] splits)
    {
        GeneratorInput input = Input();
        return input with
        {
            Files = input.Files.Select(f => f with { Rules = f.Rules.Where(keep).ToList(), References = splits }).ToList(),
        };
    }

    private static ReferenceSplit Part(string file, params string[] sections)
    {
        return new ReferenceSplit(file, sections);
    }

    [Fact]
    public void Splits_chapter_references_by_section_prefix()
    {
        GeneratorInput input = Split(r => r.Section.StartsWith("9.1", StringComparison.Ordinal) || r.Section.StartsWith("9.2", StringComparison.Ordinal),
            Part("ch09-first", "9.1"),
            Part("ch09-second", "9.2"));

        IReadOnlyList<GeneratedFile> files = SkillReferenceEmitter.Emit(input);

        string Content(string path) => files.Single(f => f.Path.EndsWith(path, StringComparison.Ordinal)).Content;
        string first = Content("references/ch09-first.md");
        string second = Content("references/ch09-second.md");
        string firstExamples = Content("references/examples/ch09-first.md");
        string secondExamples = Content("references/examples/ch09-second.md");
        Assert.Equal(4, files.Count);
        Assert.Contains("# Chapter 9: Sample Chapter — 9.1", first);
        Assert.Contains("# Chapter 9: Sample Chapter — 9.2", second);
        Assert.Contains("**FDG9001**", first);
        Assert.Contains("examples/ch09-first.md#fdg9001", first);
        Assert.DoesNotContain("**FDG9001**", second);
        Assert.Contains("## FDG9001", firstExamples);
        Assert.DoesNotContain("## FDG9001", secondExamples);
        Assert.DoesNotContain("9.2 Tool Checked", first);
        Assert.Contains("## 9.2 Tool Checked", second);
    }

    [Fact]
    public void Split_title_spans_the_first_to_the_last_section()
    {
        GeneratorInput input = Split(_ => true, Part("ch09-all", "9.1", "9.2", "9.3"));

        string main = SkillReferenceEmitter.Emit(input).Single(f => f.Path.EndsWith("references/ch09-all.md", StringComparison.Ordinal)).Content;

        Assert.Contains("# Chapter 9: Sample Chapter — 9.1–9.3", main);
    }

    [Fact]
    public void Section_prefix_matches_on_dot_boundary()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("ch09-first", "9.1"), Part("ch09-rest", "9.2", "9.3")),
            r => r.Id == "FDG9001" ? r with { Section = "9.10 Foo" } : r);

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Contains("Chapter 9: rule FDG9001 (section 9.10) matches no reference split.", exception.Message);
    }

    [Fact]
    public void Prefix_matches_deeper_sections()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("ch09-first", "9.1"), Part("ch09-rest", "9.2", "9.3")),
            r => r.Id == "FDG9001" ? r with { Section = "9.1.2 Foo" } : r);

        string main = SkillReferenceEmitter.Emit(input).Single(f => f.Path.EndsWith("references/ch09-first.md", StringComparison.Ordinal)).Content;

        Assert.Contains("**FDG9001**", main);
    }

    [Fact]
    public void Chapter_number_prefix_matches_only_the_chapter_introduction()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("ch09-first", "9", "9.1"), Part("ch09-rest", "9.2", "9.3")),
            r => r.Id == "FDG9001" ? r with { Section = "9" } : r);

        IReadOnlyList<GeneratedFile> files = SkillReferenceEmitter.Emit(input);

        string Content(string path) => files.Single(f => f.Path.EndsWith(path, StringComparison.Ordinal)).Content;
        Assert.Contains("**FDG9001**", Content("references/ch09-first.md"));
        Assert.DoesNotContain("**FDG9001**", Content("references/ch09-rest.md"));
        Assert.Contains("## 9.2 Tool Checked", Content("references/ch09-rest.md"));
    }

    [Fact]
    public void Chapter_number_prefix_does_not_cover_an_omitted_section()
    {
        GeneratorInput input = Split(_ => true, Part("a", "9", "9.1"), Part("b", "9.2"));

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Contains("(section 9.3) matches no reference split.", exception.Message);
    }

    [Fact]
    public void Nested_prefixes_in_two_splits_fail()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("ch09-a", "9.1", "9.2", "9.3"), Part("ch09-b", "9.1.2")),
            r => r.Id == "FDG9001" ? r with { Section = "9.1.2 Foo" } : r);

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Contains("matches more than one reference split (ch09-a, ch09-b)", exception.Message);
    }

    [Fact]
    public void Exact_section_matches_the_section_itself()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("ch09-a", "=9.12"), Part("ch09-b", "9.1", "9.2", "9.3")),
            r => r.Id == "FDG9001" ? r with { Section = "9.12 Foo" } : r);

        string main = SkillReferenceEmitter.Emit(input).Single(f => f.Path.EndsWith("references/ch09-a.md", StringComparison.Ordinal)).Content;

        Assert.Contains("**FDG9001**", main);
        Assert.Contains("# Chapter 9: Sample Chapter — 9.12\n", main);
    }

    [Fact]
    public void Exact_section_does_not_match_a_subsection()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("ch09-a", "=9.12"), Part("ch09-b", "9.1", "9.2", "9.3")),
            r => r.Id == "FDG9001" ? r with { Section = "9.12.3 Foo" } : r);

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Contains("Chapter 9: rule FDG9001 (section 9.12.3) matches no reference split.", exception.Message);
    }

    [Fact]
    public void Exact_section_leaves_an_unlisted_subsection_unmatched()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("a", "=9.12", "9.1", "9.2", "9.3"), Part("b", "9.12.3")),
            r => r.Id == "FDG9001" ? r with { Section = "9.12.1 Foo" } : r);

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Contains("(section 9.12.1) matches no reference split.", exception.Message);
    }

    [Fact]
    public void Exact_section_and_prefix_in_two_splits_fail()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("ch09-a", "=9.12", "9.1", "9.2", "9.3"), Part("ch09-b", "9.12")),
            r => r.Id == "FDG9001" ? r with { Section = "9.12 Foo" } : r);

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Contains("matches more than one reference split (ch09-a, ch09-b)", exception.Message);
    }

    [Fact]
    public void Rule_matching_two_splits_fails()
    {
        GeneratorInput input = Split(_ => true, Part("ch09-a", "9.1", "9.2", "9.3"), Part("ch09-b", "9.1"));

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Contains("matches more than one reference split (ch09-a, ch09-b)", exception.Message);
    }

    [Fact]
    public void Oversized_reference_file_fails_generation()
    {
        string huge = new('x', SkillReferenceEmitter.MaxReferenceBytes);
        GeneratorInput input = Edit(
            Input(),
            r => r.Id == "FDG9001" ? r with { Body = new[] { new Block(BlockKind.Paragraph, huge) } } : r);

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Matches(@"references/examples/ch09-sample\.md is \d+ bytes; the limit is 32768\. Split the chapter with references in meta\.yaml\.", exception.Message);
    }

    [Fact]
    public void Oversize_is_measured_in_utf8_bytes()
    {
        string wide = new('é', SkillReferenceEmitter.MaxReferenceBytes / 2);
        GeneratorInput input = Edit(
            Input(),
            r => r.Id == "FDG9001" ? r with { Body = new[] { new Block(BlockKind.Paragraph, wide) } } : r);

        Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));
    }

    [Fact]
    public void Chapter_without_splits_keeps_one_file_per_chapter()
    {
        string[] paths = SkillReferenceEmitter.Emit(Input()).Select(f => f.Path).ToArray();

        Assert.Equal(
            new[]
            {
                "plugins/framework-design-guidelines/skills/fdg-write/references/ch09-sample.md",
                "plugins/framework-design-guidelines/skills/fdg-write/references/examples/ch09-sample.md",
            },
            paths);
    }

    [Fact]
    public void Meta_references_reach_the_rule_files()
    {
        string rules = TempDir();
        foreach (string path in Directory.GetFiles(_fixtureRules))
        {
            File.Copy(path, Path.Combine(rules, Path.GetFileName(path)));
        }

        string meta = Path.Combine(rules, "meta.yaml");
        File.WriteAllText(
            meta,
            File.ReadAllText(meta) + """
                    references:
                      - file: ch09-a
                        sections: ["9.1", "9.2"]
                      - file: ch09-b
                        sections: ["9.3"]

                """);

        GeneratorInput input = GeneratorInput.Load(rules, "0.1.0");

        ReferenceSplit[] expected = { Part("ch09-a", "9.1", "9.2"), Part("ch09-b", "9.3") };
        Assert.Equal(expected.Select(r => r.File), input.Files.Single().References.Select(r => r.File));
        Assert.Equal(expected.Select(r => r.Sections), input.Files.Single().References.Select(r => r.Sections));
        Assert.Equal(expected.Select(r => r.File), input.Meta.Chapters.Single().References.Select(r => r.File));
    }

    private MetaFile LoadMetaWith(string references)
    {
        string rules = TempDir();
        File.WriteAllText(
            Path.Combine(rules, "meta.yaml"),
            "repositoryUrl: x\nchapters:\n  - token: \"9\"\n    file: s.xhtml\n    rules: ch09-sample.yaml\n    title: T\n" + references);
        return RuleLoader.LoadMeta(rules);
    }

    [Theory]
    [InlineData("    references:\n      - file: ch09-a\n")]
    [InlineData("    references:\n      - file: ch09-a\n        sections:\n")]
    [InlineData("    references:\n      - file: ch09-a\n        sections: []\n")]
    [InlineData("    references:\n      - sections: [\"9.1\"]\n")]
    [InlineData("    references:\n      -\n")]
    [InlineData("    references: [{ file: Bad_Name, sections: [\"9.1\"] }]\n")]
    [InlineData("    references: [{ file: ../x, sections: [\"9.1\"] }]\n")]
    public void Invalid_meta_references_fail_with_a_format_exception(string references)
    {
        Assert.Throws<FormatException>(() => LoadMetaWith(references));
    }

    [Fact]
    public void Meta_reference_sections_may_be_appendix_sections()
    {
        MetaFile meta = LoadMetaWith("    references: [{ file: appd-a, sections: [\"D.1\", \"=D.2\", \"D.10\"] }]\n");

        Assert.Equal(new[] { "D.1", "=D.2", "D.10" }, meta.Chapters.Single().References.Single().Sections);
    }

    [Fact]
    public void Appendix_section_prefix_matches_on_dot_boundary()
    {
        GeneratorInput input = Edit(
            Split(_ => true, Part("appd-first", "D.1"), Part("appd-rest", "D.2", "D.10")),
            r => r with
            {
                Section = r.Section.StartsWith("9.1", StringComparison.Ordinal) ? "D.1.1 Foo"
                    : r.Section.StartsWith("9.2", StringComparison.Ordinal) ? "D.2.1 Bar"
                    : "D.10.1 Baz",
            });

        IReadOnlyList<GeneratedFile> files = SkillReferenceEmitter.Emit(input);

        string first = files.Single(f => f.Path.EndsWith("references/appd-first.md", StringComparison.Ordinal)).Content;
        string rest = files.Single(f => f.Path.EndsWith("references/appd-rest.md", StringComparison.Ordinal)).Content;
        Assert.Contains("## D.1.1 Foo", first);
        Assert.DoesNotContain("D.10.1 Baz", first);
        Assert.Contains("## D.10.1 Baz", rest);
        Assert.Contains("— D.2–D.10", rest);
    }

    [Fact]
    public void Null_meta_references_mean_no_split()
    {
        Assert.Empty(LoadMetaWith("    references:\n").Chapters.Single().References);
    }

    [Fact]
    public void Reference_stem_used_twice_fails_generation()
    {
        GeneratorInput duplicate = Split(_ => true, Part("ch09-a", "9.1", "9.2"), Part("ch09-a", "9.3"));
        GeneratorInput first = Input();
        RuleFile other = first.Files.Single() with { Chapter = "10" };
        GeneratorInput clash = first with { Files = new[] { first.Files.Single(), other } };

        Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(duplicate));
        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(clash));
        Assert.Contains("Chapter 10: reference file 'ch09-sample' is already used by Chapter 9.", exception.Message);
    }

    [Fact]
    public void Split_matching_no_rule_fails_generation()
    {
        GeneratorInput input = Split(_ => true, Part("ch09-a", "9.1", "9.2", "9.3"), Part("ch09-empty", "9.4"));

        GenerationException exception = Assert.Throws<GenerationException>(() => SkillReferenceEmitter.Emit(input));

        Assert.Equal("Chapter 9: reference split 'ch09-empty' matches no rule.", exception.Message);
    }

    [Fact]
    public void Split_title_orders_sections_numerically()
    {
        GeneratorInput input = Split(_ => true, Part("ch09-all", "9.3", "9.1", "9.2"));

        string main = SkillReferenceEmitter.Emit(input).Single(f => f.Path.EndsWith("references/ch09-all.md", StringComparison.Ordinal)).Content;

        Assert.Contains("# Chapter 9: Sample Chapter — 9.1–9.3", main);
    }

    [Fact]
    public void Section_text_is_escaped_where_it_becomes_markdown()
    {
        GeneratorInput input = Edit(Input(), r => r.Id == "FDG9001" ? r with { Section = "9.1 IComparable<T> and IEquatable<T>" } : r);

        string main = ReferenceMain(input);
        string page = RuleDocEmitter.Emit(input).Single(f => f.Path == "docs/rules/FDG9001.md").Content;

        Assert.Contains("## 9.1 IComparable&lt;T> and IEquatable&lt;T>", main);
        Assert.Contains("section 9.1 IComparable&lt;T> and IEquatable&lt;T>.", page);
        Assert.DoesNotContain("IComparable<T>", main);
        Assert.DoesNotContain("IComparable<T>", page);
    }

    [Fact]
    public void Skill_reference_groups_rules_by_section_and_links_examples()
    {
        GeneratorInput input = Input();
        string main = ReferenceMain(input);
        GeneratedFile examples = SkillReferenceEmitter.Emit(input)
            .Single(f => f.Path.EndsWith("references/examples/ch09-sample.md", StringComparison.Ordinal));

        Assert.Contains("## 9.1 Judgment Calls", main);
        Assert.Contains("## 9.2 Tool Checked", main);
        Assert.Contains("## FDG9001", examples.Content);
        Assert.Contains("Records give value equality for free.", examples.Content);
        Assert.Contains("public record Point(int X, int Y);", examples.Content);
        Assert.StartsWith("<!-- <auto-generated> by fdg-gen from rules/*.yaml", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptor_uses_help_link_from_meta()
    {
        string source = DescriptorEmitter.Emit(Input());

        Assert.Contains("public static readonly DiagnosticDescriptor FDG9007", source);
        Assert.Contains("\"https://example.test/repo/blob/main/docs/rules/FDG9007.md\"", source);
        Assert.Contains("\"DO put each statement on its own line.\"", source);
        Assert.Contains("\"Put each statement on its own line\"", source);
        Assert.Contains("\"FrameworkDesignGuidelines.Layout\"", source);
        Assert.Contains("DiagnosticSeverity.Warning", source);
        Assert.Contains("isEnabledByDefault: false", source);
        Assert.Contains("internal static class FdgDescriptors", source);
        Assert.DoesNotContain("FDG9002", source);
    }

    [Fact]
    public void Rules_json_lists_breaking_fix_diagnostics()
    {
        JsonObject json = JsonNode.Parse(RulesJsonEmitter.Emit(Input()))!.AsObject();

        Assert.Equal(1, json["protocol"]!.GetValue<int>());
        Assert.Equal("0.1.0", json["version"]!.GetValue<string>());
        Assert.Equal(new[] { "CA1710" }, json["breakingFixDiagnostics"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(
            new[] { "CA1000", "CA1710", "CA1716", "FDG9007", "IDE1006" },
            json["registeredDiagnostics"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void Diagnostic_is_breaking_if_any_mapping_rule_is_breaking()
    {
        // CA1710 is mapped by FDG9004 (autofix no) and FDG9005 (breaking); IDE1006 only by non-breaking rules.
        GeneratorInput input = Input();
        JsonObject json = JsonNode.Parse(RulesJsonEmitter.Emit(input))!.AsObject();
        Assert.Contains("CA1710", json["breakingFixDiagnostics"]!.AsArray().Select(n => n!.GetValue<string>()));

        GeneratorInput noBreaking = Edit(input, OnRule("FDG9005", m => m with { Autofix = AutofixKind.No }));
        JsonObject none = JsonNode.Parse(RulesJsonEmitter.Emit(noBreaking))!.AsObject();
        Assert.Empty(none["breakingFixDiagnostics"]!.AsArray());

        GeneratorInput shared = Edit(input, OnRule("FDG9003", m => m with { Autofix = AutofixKind.Yes }));
        shared = Edit(shared, OnRule("FDG9009", m => m with { Autofix = AutofixKind.Breaking }));
        JsonObject sharedJson = JsonNode.Parse(RulesJsonEmitter.Emit(shared))!.AsObject();
        Assert.Contains("IDE1006", sharedJson["breakingFixDiagnostics"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void Rules_json_carries_naming_tags()
    {
        var tags = new NamingTags(new[] { "parameter", "local" }, new[] { "casing" });
        GeneratorInput input = Edit(Input(), OnRule("FDG9003", m => m with { Naming = tags }));

        JsonObject json = JsonNode.Parse(RulesJsonEmitter.Emit(input))!.AsObject();
        JsonObject[] rules = json["rules"]!.AsArray().Select(n => n!.AsObject()).ToArray();

        JsonObject naming = rules.Single(r => r["id"]!.GetValue<string>() == "FDG9003")["naming"]!.AsObject();
        Assert.Equal(new[] { "parameter", "local" }, naming["symbols"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(new[] { "casing" }, naming["failures"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(1, json["protocol"]!.GetValue<int>());
        Assert.All(rules.Where(r => r["id"]!.GetValue<string>() != "FDG9003"), r => Assert.Null(r["naming"]));
        Assert.All(rules, r => Assert.True(r.ContainsKey("naming")));
    }

    [Fact]
    public void Rules_json_describes_each_rule()
    {
        JsonObject json = JsonNode.Parse(RulesJsonEmitter.Emit(Input()))!.AsObject();

        JsonObject rule = json["rules"]!.AsArray().Select(n => n!.AsObject()).Single(r => r["id"]!.GetValue<string>() == "FDG9002");

        Assert.Equal("DO NOT", rule["strength"]!.GetValue<string>());
        Assert.Equal("use identifiers that match language keywords.", rule["text"]!.GetValue<string>());
        Assert.Equal("9.1 Judgment Calls", rule["section"]!.GetValue<string>());
        Assert.Equal(new[] { "CA1716" }, rule["diagnostics"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("no", rule["autofix"]!.GetValue<string>());
        Assert.Equal("warning", rule["severity"]!["library"]!.GetValue<string>());
        Assert.Equal("none", rule["severity"]!["test"]!.GetValue<string>());
    }

    [Fact]
    public void Rule_page_lists_book_text_and_mapping()
    {
        GeneratedFile page = RuleDocEmitter.Emit(Input()).Single(f => f.Path == "docs/rules/FDG9001.md");

        Assert.Contains("DO prefer records for immutable data carriers.", page.Content);
        Assert.Contains("Records give value equality for free.", page.Content);
        Assert.Contains("[C# 9+]", page.Content);
        Assert.StartsWith("<!-- <auto-generated> by fdg-gen from rules/*.yaml", page.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Appendix_d_verdict_is_set_off_from_the_heading_text_by_a_colon()
    {
        GeneratorInput input = Edit(Input(), rule => rule.Id == "FDG9001"
            ? rule with { Strength = Strength.NotPermitted, Text = "Changing the Name of an Assembly" }
            : rule);

        GeneratedFile page = RuleDocEmitter.Emit(input).Single(f => f.Path == "docs/rules/FDG9001.md");

        Assert.Contains("> NOT PERMITTED: Changing the Name of an Assembly\n", page.Content);
        Assert.Contains("- **FDG9001** NOT PERMITTED: Changing the Name of an Assembly", ReferenceMain(input));
        JsonObject json = JsonNode.Parse(RulesJsonEmitter.Emit(input))!.AsObject();
        JsonObject rule = json["rules"]!.AsArray().Select(n => n!.AsObject())
            .Single(r => r["id"]!.GetValue<string>() == "FDG9001");
        Assert.Equal("NOT PERMITTED", rule["strength"]!.GetValue<string>());
        Assert.Equal("Changing the Name of an Assembly", rule["text"]!.GetValue<string>());
    }

    [Fact]
    public void Appendix_d_verdict_context_is_the_breaks_it_can_cause()
    {
        GeneratorInput input = Edit(Input(), rule => rule.Id == "FDG9001"
            ? rule with { Strength = Strength.NotPermitted, Context = "runtime break" }
            : rule);

        string page = RuleDocEmitter.Emit(input).Single(f => f.Path == "docs/rules/FDG9001.md").Content;
        string reference = ReferenceMain(input);

        Assert.Contains(" Can cause: runtime break.\n", page);
        Assert.DoesNotContain("Applies to:", page);
        Assert.Contains("- **FDG9001** NOT PERMITTED: ", reference);
        Assert.Contains(" (can cause: runtime break)", reference);
        Assert.DoesNotContain("(context: runtime break)", reference);
    }

    [Fact]
    public void Guideline_context_is_where_the_rule_applies()
    {
        GeneratorInput input = Edit(Input(), rule => rule.Id == "FDG9001"
            ? rule with { Context = "public types" }
            : rule);

        string page = RuleDocEmitter.Emit(input).Single(f => f.Path == "docs/rules/FDG9001.md").Content;
        string reference = ReferenceMain(input);

        Assert.Contains(" Applies to: public types.\n", page);
        Assert.DoesNotContain("Can cause:", page);
        Assert.Contains(" (context: public types)", reference);
        Assert.DoesNotContain("(can cause:", reference);
    }

    [Fact]
    public void Coverage_counts_each_chapter_and_lists_non_guideline_content()
    {
        string coverage = CoverageEmitter.Emit(Input());

        Assert.Contains("| 9 Sample Chapter | 10 | 6 | 1 | 2 | 1 | 10 |", coverage);
        Assert.Contains("Non-guideline content", coverage);
        Assert.Contains("9.1", coverage);
        Assert.Contains("Narrative introduction to the sample chapter.", coverage);
    }

    [Fact]
    public void Generate_is_idempotent()
    {
        string root = TempDir();

        int first = GeneratedFiles.Write(root, GeneratedFiles.Create(Input()));
        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        var times = files.ToDictionary(f => f, File.GetLastWriteTimeUtc);
        Thread.Sleep(50);
        int second = GeneratedFiles.Write(root, GeneratedFiles.Create(Input()));

        Assert.True(first > 0);
        Assert.Equal(0, second);
        Assert.Equal(files, Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        Assert.All(files, f => Assert.Equal(times[f], File.GetLastWriteTimeUtc(f)));
    }

    [Fact]
    public void Write_removes_stale_reference_files()
    {
        string root = TempDir();
        GeneratedFiles.Write(root, GeneratedFiles.Create(Input()));
        string skill = Path.Combine(root, "plugins", "framework-design-guidelines", "skills", "fdg-write");
        string references = Path.Combine(skill, "references");
        string[] stale = { Path.Combine(references, "old.md"), Path.Combine(references, "examples", "old.md") };
        string[] kept = { Path.Combine(references, "keep.txt"), Path.Combine(skill, "SKILL.md") };
        foreach (string path in stale.Concat(kept))
        {
            File.WriteAllText(path, "hand-made");
        }

        // Every generated file already matches the disk, so the count is exactly the two removals.
        int changes = GeneratedFiles.Write(root, GeneratedFiles.Create(Input()));

        Assert.All(stale, path => Assert.False(File.Exists(path), path));
        Assert.All(kept, path => Assert.True(File.Exists(path), path));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Write_reports_the_removed_files_separately()
    {
        string root = TempDir();
        IReadOnlyList<GeneratedFile> files = GeneratedFiles.Create(Input());
        GeneratedFiles.Write(root, files, out int removedOnFirstWrite);
        File.WriteAllText(Path.Combine(root, "docs", "rules", "FDG0000.md"), "hand-made");

        int changes = GeneratedFiles.Write(root, files, out int removed);

        Assert.Equal(0, removedOnFirstWrite);
        Assert.Equal(1, removed);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Generate_command_reports_written_removed_and_unchanged_counts()
    {
        string root = TempDir();
        string[] args = { "generate", "--rules", _fixtureRules, "--out", root, "--version", "0.1.0" };
        var first = new StringWriter();
        GeneratorCli.Run(args, first, new StringWriter());
        int count = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
        File.WriteAllText(Path.Combine(root, "docs", "rules", "FDG0000.md"), "hand-made");
        var second = new StringWriter();

        int exitCode = GeneratorCli.Run(args, second, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains($"Generated {count} files under {root}: {count} written, 0 removed, 0 unchanged.", first.ToString());
        Assert.Contains($"{count} files under {root}: 0 written, 1 removed, {count} unchanged.", second.ToString());
    }

    [Fact]
    public void Generated_files_use_lf_and_carry_a_header_where_comments_are_allowed()
    {
        IReadOnlyList<GeneratedFile> files = GeneratedFiles.Create(Input());

        Assert.All(files, f => Assert.DoesNotContain('\r', f.Content));
        Assert.All(files, f => Assert.EndsWith("\n", f.Content, StringComparison.Ordinal));
        Assert.All(
            files.Where(f => !f.Path.EndsWith(".json", StringComparison.Ordinal)),
            f => Assert.Contains("<auto-generated> by fdg-gen from rules/*.yaml — do not edit", f.Content));
        Assert.All(
            files.Where(f => f.Path.EndsWith(".json", StringComparison.Ordinal)),
            f => Assert.DoesNotContain("auto-generated", f.Content));
    }

    [Fact]
    public void Generate_command_writes_every_output_to_the_repository_layout()
    {
        string root = TempDir();
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = GeneratorCli.Run(
            new[] { "generate", "--rules", _fixtureRules, "--out", root, "--version", "0.1.0" },
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        foreach (string path in new[]
        {
            "src/FrameworkDesignGuidelines.Analyzers/Generated/FdgDescriptors.g.cs",
            "src/FrameworkDesignGuidelines.Package/build/globalconfigs/fdg.library.globalconfig",
            "src/FrameworkDesignGuidelines.Package/build/globalconfigs/fdg.application.globalconfig",
            "src/FrameworkDesignGuidelines.Package/build/globalconfigs/fdg.test.globalconfig",
            "src/FrameworkDesignGuidelines.Package/data/rules.json",
            "plugins/framework-design-guidelines/skills/fdg-write/references/ch09-sample.md",
            "plugins/framework-design-guidelines/skills/fdg-write/references/examples/ch09-sample.md",
            "docs/rules/FDG9001.md",
            "docs/coverage.md",
        })
        {
            Assert.True(File.Exists(Path.Combine(root, path)), path);
        }
    }

    [Fact]
    public void Generate_command_reports_a_config_conflict()
    {
        string rulesDir = TempDir();
        foreach (string file in Directory.GetFiles(_fixtureRules))
        {
            File.Copy(file, Path.Combine(rulesDir, Path.GetFileName(file)));
        }

        string sample = Path.Combine(rulesDir, "ch09-sample.yaml");
        File.WriteAllText(sample, File.ReadAllText(sample).Replace("indent_size = 4", "indent_size = 2").Replace("config: []\n  minLanguage: \"9\"", "config:\n    - \"indent_size = 4\"\n  minLanguage: \"9\""));
        var error = new StringWriter();

        int exitCode = GeneratorCli.Run(new[] { "generate", "--rules", rulesDir, "--out", TempDir(), "--version", "0.1.0" }, new StringWriter(), error);

        Assert.Equal(1, exitCode);
        Assert.Contains("FDG9001", error.ToString());
        Assert.Contains("FDG9010", error.ToString());
    }
}
