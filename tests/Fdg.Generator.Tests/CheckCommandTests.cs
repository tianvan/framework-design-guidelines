using System.Text.Json.Nodes;
using Fdg.Generator.Commands;
using Fdg.Generator.Plugin;
using Fdg.Generator.Rules;
using Fdg.Generator.Sdk;
using Microsoft.CodeAnalysis;

namespace Fdg.Generator.Tests;

public sealed class CheckCommandTests : IDisposable, IClassFixture<InstalledSdkFixture>
{
    private const string RulesFileName = "ch09-sample.yaml";

    private static readonly IReadOnlyDictionary<string, SdkDiagnostic> FakeCatalog = new[]
    {
        new SdkDiagnostic("CA1707", "Identifiers should not contain underscores", DiagnosticSeverity.Hidden, HasCodeFix: true),
        new SdkDiagnostic("CA1716", "Identifiers should not match keywords", DiagnosticSeverity.Hidden, HasCodeFix: false),
        new SdkDiagnostic("IDE1006", "Naming rule violation", DiagnosticSeverity.Hidden, HasCodeFix: true),
        new SdkDiagnostic("IDE0055", "Fix formatting", DiagnosticSeverity.Hidden, HasCodeFix: true),
    }.ToDictionary(d => d.Id);

    private const string ClaudeManifest = "plugins/framework-design-guidelines/.claude-plugin/plugin.json";
    private const string CodexManifest = "plugins/framework-design-guidelines/.codex-plugin/plugin.json";
    private const string Marketplace = ".claude-plugin/marketplace.json";

    private readonly InstalledSdkFixture _sdk;
    private readonly string _root;
    private readonly string _rulesDir;
    private readonly string _bookDir = Path.Combine(AppContext.BaseDirectory, "fixtures", "book");

    public CheckCommandTests(InstalledSdkFixture sdk)
    {
        _sdk = sdk;
        _root = Path.Combine(Path.GetTempPath(), "fdg-check-tests-" + Guid.NewGuid().ToString("N"));
        _rulesDir = Path.Combine(_root, "rules");
        Directory.CreateDirectory(_rulesDir);
        WriteRootFile("Directory.Build.props", "<Project>\n  <PropertyGroup>\n    <Version>0.1.0</Version>\n  </PropertyGroup>\n</Project>\n");
        WriteRootFile(ClaudeManifest, "{ \"name\": \"framework-design-guidelines\", \"version\": \"0.1.0\" }\n");
        WriteRootFile(CodexManifest, "{ \"name\": \"framework-design-guidelines\", \"version\": \"0.1.0\" }\n");
        WriteRootFile(
            Marketplace,
            "{ \"name\": \"framework-design-guidelines\", \"version\": \"0.1.0\", \"plugins\": " +
            "[ { \"name\": \"framework-design-guidelines\", \"version\": \"0.1.0\" } ] }\n");
        File.WriteAllText(
            Path.Combine(_rulesDir, "meta.yaml"),
            "repositoryUrl: https://example.test/repo\n" +
            "chapters:\n" +
            "  - { token: \"9\", file: sample.xhtml, rules: ch09-sample.yaml, title: Sample Chapter }\n");
        foreach (string name in new[] { "rules.schema.json", "profiles.yaml" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "rules", name), Path.Combine(_rulesDir, name));
        }

        Assert.Equal(0, Run("extract", "--book", _bookDir, "--chapter", "9", "--rules", _rulesDir).ExitCode);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private void WriteRootFile(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void EditRootFile(string relativePath, Func<string, string> edit)
    {
        string path = Path.Combine(_root, relativePath);
        File.WriteAllText(path, edit(File.ReadAllText(path)));
    }

    private string RulesPath => Path.Combine(_rulesDir, RulesFileName);

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = GeneratorCli.Run(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private IReadOnlyList<string> Check()
    {
        return CheckCommand.Check(_rulesDir, FakeCatalog).Select(e => e.ToString()).ToList();
    }

    private void EditRules(Func<string, string> edit)
    {
        File.WriteAllText(RulesPath, edit(File.ReadAllText(RulesPath)));
    }

    private void Map(string id, Func<RuleMapping, RuleMapping> map)
    {
        IReadOnlyList<Rule> rules = RuleLoader.LoadRules(RulesPath);
        File.WriteAllText(
            RulesPath,
            RuleYaml.Serialize(rules.Select(r => r.Id == id ? r with { Mapping = map(r.Mapping) } : r)));
    }

    private static RuleMapping Sdk(RuleMapping mapping, params string[] diagnostics)
    {
        return mapping with { Check = new CheckInfo(CheckKind.Sdk, diagnostics, CoverageLevel.Full) };
    }

    private static RuleMapping Fdg(RuleMapping mapping, AnalyzerInfo? analyzer, params string[] diagnostics)
    {
        return mapping with
        {
            Check = new CheckInfo(CheckKind.Fdg, diagnostics, CoverageLevel.Full),
            Analyzer = analyzer,
        };
    }

    [Fact]
    public void Untouched_extraction_passes_check()
    {
        Assert.Empty(Check());
    }

    private void SetChapterTestProfile(string value)
    {
        File.WriteAllText(
            Path.Combine(_rulesDir, "meta.yaml"),
            "repositoryUrl: https://example.test/repo\n" +
            "chapters:\n" +
            $"  - {{ token: \"9\", file: sample.xhtml, rules: ch09-sample.yaml, title: Sample Chapter, testProfile: {value} }}\n");
    }

    [Fact]
    public void Test_severity_must_be_none_in_chapters_off_for_tests()
    {
        SetChapterTestProfile("off");
        Map("FDG9002", m => m with { Severity = m.Severity with { Test = SeverityLevel.None } });

        IReadOnlyList<string> errors = Check();

        Assert.Equal(3, errors.Count);
        Assert.All(errors, e => Assert.Contains("chapter 9 is off in the test profile", e));
        Assert.DoesNotContain(errors, e => e.Contains("FDG9002"));
        Assert.Equal(
            $"{RulesFileName}:FDG9001: chapter 9 is off in the test profile; " +
            "set severity.test to none or explain it in notes with \"Test profile:\".",
            errors[0]);
    }

    [Fact]
    public void Test_profile_exception_is_allowed_with_a_note()
    {
        SetChapterTestProfile("off");
        foreach (string id in new[] { "FDG9001", "FDG9002", "FDG9003", "FDG9004" })
        {
            Map(id, m => m with { Notes = "Test profile: the rule also holds for test code." });
        }

        Assert.Empty(Check());
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("\"\"")]
    public void Test_profile_value_must_be_on_or_off(string value)
    {
        SetChapterTestProfile(value);

        string error = Assert.Single(Check());
        Assert.StartsWith("meta.yaml:", error);
        Assert.Contains("testProfile of chapter 9 must be on or off", error);
    }

    [Fact]
    public void Chapters_are_on_for_tests_unless_the_meta_says_otherwise()
    {
        Assert.Empty(Check());
        SetChapterTestProfile("on");
        Assert.Empty(Check());
    }

    [Fact]
    public void Schema_rejects_unknown_strength()
    {
        EditRules(yaml => yaml.Replace("strength: CONSIDER", "strength: SHOULD"));

        string error = Assert.Single(Check(), e => e.Contains("strength"));
        Assert.StartsWith($"{RulesFileName}:FDG9003: ", error);
    }

    [Fact]
    public void Schema_errors_name_the_rule_and_field()
    {
        EditRules(yaml => yaml.Replace("  autofix: no\n", "  autofix: maybe\n"));

        IReadOnlyList<string> errors = Check();

        Assert.Equal(4, errors.Count);
        Assert.All(errors, e => Assert.Contains("/autofix", e));
        Assert.Contains(errors, e => e.StartsWith($"{RulesFileName}:FDG9001: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Yaml_scalars_follow_yaml_1_2_core_schema()
    {
        JsonNode? node = YamlJsonConverter.ToJson(
            "autofix: no\nother: yes\nstyle: false\ncontext: null\nquoted: \"true\"\nlevel: 7.3\n");

        JsonObject json = Assert.IsType<JsonObject>(node);
        Assert.Equal("no", json["autofix"]!.GetValue<string>());
        Assert.Equal("yes", json["other"]!.GetValue<string>());
        Assert.False(json["style"]!.GetValue<bool>());
        Assert.Null(json["context"]);
        Assert.True(json.ContainsKey("context"));
        Assert.Equal("true", json["quoted"]!.GetValue<string>());
        Assert.Equal(7.3, json["level"]!.GetValue<double>());
    }

    [Fact]
    public void Unknown_sdk_diagnostic_is_reported()
    {
        Map("FDG9001", m => Sdk(m, "CA9999"));

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("CA9999", error);
    }

    [Fact]
    public void Sdk_kind_requires_diagnostics_and_coverage()
    {
        Map("FDG9001", m => m with { Check = new CheckInfo(CheckKind.Sdk, Array.Empty<string>(), CoverageLevel.None) });

        IReadOnlyList<string> errors = Check();

        Assert.Contains(errors, e => e.Contains("FDG9001") && e.Contains("diagnostics"));
        Assert.Contains(errors, e => e.Contains("FDG9001") && e.Contains("coverage"));
    }

    [Fact]
    public void Judgment_kind_must_not_list_diagnostics()
    {
        Map("FDG9001", m => m with { Check = new CheckInfo(CheckKind.Judgment, new[] { "CA1707" }, CoverageLevel.None) });

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("judgment", error);
    }

    [Fact]
    public void Fdg_kind_must_include_own_id()
    {
        var analyzer = new AnalyzerInfo("Expand the block", "Layout");
        Map("FDG9001", m => Fdg(m, analyzer, "CA1707"));
        Map("FDG9002", m => Fdg(m, analyzer, "FDG9002", "CA1707"));

        string error = Assert.Single(Check());
        Assert.StartsWith($"{RulesFileName}:FDG9001: ", error);
        Assert.Contains("FDG9001", error[$"{RulesFileName}:FDG9001: ".Length..]);
    }

    [Fact]
    public void Fdg_kind_requires_analyzer_message()
    {
        Map("FDG9001", m => Fdg(m, analyzer: null, "FDG9001"));

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("analyzer", error);
    }

    [Fact]
    public void Analyzer_is_only_allowed_for_fdg_kind()
    {
        Map("FDG9001", m => m with { Analyzer = new AnalyzerInfo("Message", "Layout") });

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("analyzer", error);
    }

    [Fact]
    public void Duplicate_rule_id_is_reported()
    {
        EditRules(yaml => yaml.Replace("id: FDG9002", "id: FDG9001"));

        IReadOnlyList<string> errors = Check();

        Assert.Contains(errors, e => e.StartsWith($"{RulesFileName}:FDG9001: ", StringComparison.Ordinal) && e.Contains("duplicate"));
    }

    [Fact]
    public void Rule_id_must_belong_to_its_chapter()
    {
        EditRules(yaml => yaml.Replace("id: FDG9002", "id: FDG8002"));

        string error = Assert.Single(Check());
        Assert.Contains("FDG8002", error);
        Assert.Contains("chapter 9", error);
    }

    [Fact]
    public void Consider_with_warning_severity_is_allowed_but_default_is_suggestion()
    {
        RuleMapping defaults = RuleMapping.CreateDefault(Strength.Consider, style: false, testProfileOn: true);
        Assert.Equal(new ProfileSeverity(SeverityLevel.Suggestion, SeverityLevel.Suggestion, SeverityLevel.Suggestion), defaults.Severity);
        Assert.Equal(
            new ProfileSeverity(SeverityLevel.Warning, SeverityLevel.Warning, SeverityLevel.Warning),
            RuleMapping.CreateDefault(Strength.Avoid, style: false, testProfileOn: true).Severity);
        Rule consider = Assert.Single(RuleLoader.LoadRules(RulesPath), r => r.Strength == Strength.Consider);
        Assert.Equal(defaults.Severity, consider.Mapping.Severity);

        var warning = new ProfileSeverity(SeverityLevel.Warning, SeverityLevel.Warning, SeverityLevel.Warning);
        Map(consider.Id, m => m with { Severity = warning });

        Assert.Empty(Check());
    }

    [Fact]
    public void Error_severity_is_rejected()
    {
        EditRules(yaml => yaml.Replace("    library: suggestion\n", "    library: error\n"));

        string error = Assert.Single(Check());
        Assert.Contains("FDG9003", error);
        Assert.Contains("/severity/library", error);
    }

    [Fact]
    public void Autofix_requires_a_code_fix()
    {
        Map("FDG9001", m => Sdk(m, "CA1716") with { Autofix = AutofixKind.Yes });
        Map("FDG9002", m => Sdk(m, "CA1707") with { Autofix = AutofixKind.Breaking });

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("code fix", error);
    }

    [Fact]
    public void Style_flag_is_reserved_for_appendix_a()
    {
        Map("FDG9001", m => m with { Style = true });

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("style", error);
    }

    [Fact]
    public void Alternative_requires_min_language()
    {
        Map("FDG9001", m => m with { Alternative = "Use a try/finally block." });

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("minLanguage", error);
    }

    [Fact]
    public void Config_lines_must_be_editorconfig_assignments()
    {
        Map("FDG9001", m => m with { Config = new[] { "indent_size 4" } });

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("indent_size 4", error);
    }

    [Fact]
    public void Config_must_not_set_severities()
    {
        Map("FDG9001", m => Sdk(m, "IDE0055") with
        {
            Config = new[] { "dotnet_diagnostic.IDE0055.severity = warning", "csharp_prefer_braces = true:warning" },
        });

        IReadOnlyList<string> errors = Check();

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Contains("severity", e));
    }

    [Fact]
    public void Conflicting_config_values_are_reported()
    {
        Map("FDG9001", m => Sdk(m, "IDE0055") with { Config = new[] { "indent_size = 4", "indent_style = space" } });
        Map("FDG9002", m => Sdk(m, "IDE0055") with { Config = new[] { "indent_size = 4" } });
        Map("FDG9004", m => Sdk(m, "IDE0055") with { Config = new[] { "indent_size = 2" } });

        string error = Assert.Single(Check());
        Assert.StartsWith($"{RulesFileName}:FDG9004: ", error);
        Assert.Contains("indent_size", error);
        Assert.Contains("FDG9001", error);
    }

    [Fact]
    public void Config_must_not_override_profile_options()
    {
        Map("FDG9001", m => m with { Config = new[] { "dotnet_code_quality.api_surface = all" } });

        string error = Assert.Single(Check());
        Assert.Contains("FDG9001", error);
        Assert.Contains("profiles.yaml", error);
    }

    private static readonly string[] ValidNaming =
    {
        "dotnet_naming_rule.fdg9001_types.symbols = fdg_types",
        "dotnet_naming_rule.fdg9001_types.style = fdg_pascal_case",
        "dotnet_naming_symbols.fdg_types.applicable_kinds = class, struct, interface, enum, delegate",
        "dotnet_naming_symbols.fdg_types.applicable_accessibilities = *",
        "dotnet_naming_style.fdg_pascal_case.capitalization = pascal_case",
    };

    [Fact]
    public void Valid_naming_config_passes()
    {
        Map("FDG9001", m => Sdk(m, "IDE1006") with { Config = ValidNaming });

        Assert.Empty(Check());
    }

    [Fact]
    public void Naming_rule_name_must_start_with_rule_id()
    {
        string[] config = ValidNaming.Select(c => c.Replace("fdg9001_types", "types")).ToArray();
        Map("FDG9001", m => Sdk(m, "IDE1006") with { Config = config });

        IReadOnlyList<string> errors = Check();

        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Contains("fdg9001", e));
    }

    [Fact]
    public void Naming_rule_must_map_to_ide1006()
    {
        Map("FDG9001", m => Sdk(m, "CA1707") with { Config = ValidNaming });

        string error = Assert.Single(Check());
        Assert.Contains("IDE1006", error);
    }

    [Fact]
    public void Naming_config_rejects_unknown_values_and_references()
    {
        string[] config =
        {
            "dotnet_naming_rule.fdg9001_types.symbols = fdg_missing",
            "dotnet_naming_rule.fdg9001_types.style = fdg_pascal_case",
            "dotnet_naming_rule.fdg9001_types.severity = warning",
            "dotnet_naming_symbols.fdg_types.applicable_kinds = class, structure",
            "dotnet_naming_style.fdg_pascal_case.capitalization = PascalCase",
        };
        Map("FDG9001", m => Sdk(m, "IDE1006") with { Config = config });

        IReadOnlyList<string> errors = Check();

        Assert.Contains(errors, e => e.Contains("fdg_missing"));
        Assert.Contains(errors, e => e.Contains("severity"));
        Assert.Contains(errors, e => e.Contains("structure"));
        Assert.Contains(errors, e => e.Contains("PascalCase"));
    }

    [Fact]
    public void Profiles_must_define_every_profile()
    {
        File.WriteAllText(Path.Combine(_rulesDir, "profiles.yaml"), "library: []\napplication: []\ntests: []\n");

        IReadOnlyList<string> errors = Check();

        Assert.Contains(errors, e => e.StartsWith("profiles.yaml:", StringComparison.Ordinal) && e.Contains("'test'"));
        Assert.Contains(errors, e => e.StartsWith("profiles.yaml:", StringComparison.Ordinal) && e.Contains("'tests'"));
    }

    [Fact]
    public void Versions_in_manifests_match_directory_build_props()
    {
        Assert.Empty(ManifestVersionValidator.Validate(InstalledSdkFixture.RepositoryRoot()));

        EditRootFile(CodexManifest, json => json.Replace("\"version\": \"0.1.0\"", "\"version\": \"0.2.0\""));
        EditRootFile(Marketplace, json => json.Replace("\"0.1.0\" } ]", "\"0.0.9\" } ]"));
        File.Delete(Path.Combine(_root, ClaudeManifest));

        IReadOnlyList<string> errors = Check();

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.StartsWith($"{CodexManifest}:-: ", StringComparison.Ordinal) && e.Contains("0.2.0") && e.Contains("0.1.0"));
        Assert.Contains(errors, e => e.StartsWith($"{Marketplace}:-: ", StringComparison.Ordinal) && e.Contains("0.0.9"));
        Assert.Contains(errors, e => e.StartsWith($"{ClaudeManifest}:-: ", StringComparison.Ordinal) && e.Contains("does not exist"));

        EditRootFile("Directory.Build.props", props => props.Replace("0.1.0", "0.2.0"));
        WriteRootFile(ClaudeManifest, "{ \"name\": \"framework-design-guidelines\" }\n");

        errors = Check();

        Assert.Contains(errors, e => e.StartsWith($"{ClaudeManifest}:-: ", StringComparison.Ordinal) && e.Contains("version"));
        Assert.DoesNotContain(errors, e => e.StartsWith($"{CodexManifest}:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith($"{Marketplace}:-: ", StringComparison.Ordinal) && e.Contains("0.1.0"));

        var result = Run("check", "--rules", _rulesDir, "--sdk", _sdk.Directory);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{ClaudeManifest}:-: ", result.Error);
    }

    [Fact]
    public void Check_command_accepts_rules_directory_with_trailing_separator()
    {
        // Shell completion writes `--rules rules/`; the repository root must still be the rules directory's parent.
        Assert.True(RulesDirectory.TryResolve(_rulesDir + "/", out string resolved, out _));
        Assert.Equal(_rulesDir, resolved);

        var result = Run("check", "--rules", _rulesDir + "/", "--sdk", _sdk.Directory);

        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Catalog_loads_netanalyzers_and_codestyle_from_installed_sdk()
    {
        IReadOnlyDictionary<string, SdkDiagnostic> catalog = _sdk.Catalog;

        foreach (string id in new[] { "CA1707", "CA1716", "IDE1006", "IDE0011", "IDE0055" })
        {
            Assert.True(catalog.ContainsKey(id), $"{id} is missing from the catalog.");
        }

        Assert.True(catalog["IDE1006"].HasCodeFix);
        Assert.True(catalog["CA1707"].HasCodeFix);
        Assert.False(catalog["CA1716"].HasCodeFix);
        Assert.False(string.IsNullOrEmpty(catalog["CA1707"].Title));
    }

    [Fact]
    public void Check_command_reports_every_error_and_fails()
    {
        Map("FDG9001", m => Sdk(m, "CA9999"));
        Map("FDG9002", m => Sdk(m, "CA9998"));

        var result = Run("check", "--rules", _rulesDir, "--sdk", _sdk.Directory);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"{RulesFileName}:FDG9001: ", result.Error);
        Assert.Contains($"{RulesFileName}:FDG9002: ", result.Error);
    }

    [Fact]
    public void Check_command_with_book_also_verifies_source()
    {
        Assert.Equal(0, Run("check", "--rules", _rulesDir, "--sdk", _sdk.Directory, "--book", _bookDir).ExitCode);

        EditRules(yaml => yaml.Replace("keeping names short.", "keeping names tiny."));

        var result = Run("check", "--rules", _rulesDir, "--sdk", _sdk.Directory, "--book", _bookDir);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("FDG9003", result.Error);
        Assert.Contains("text", result.Error);
    }
}

/// <summary>Locates the SDK selected by <c>global.json</c> and loads its catalog once per test class.</summary>
public sealed class InstalledSdkFixture
{
    private readonly Lazy<string> _directory = new(() => SdkLocator.FindSdkDirectory(RepositoryRoot()));
    private readonly Lazy<IReadOnlyDictionary<string, SdkDiagnostic>> _catalog;

    public InstalledSdkFixture()
    {
        _catalog = new Lazy<IReadOnlyDictionary<string, SdkDiagnostic>>(() => SdkDiagnosticCatalog.Load(Directory));
    }

    public string Directory => _directory.Value;

    public IReadOnlyDictionary<string, SdkDiagnostic> Catalog => _catalog.Value;

    public static string RepositoryRoot()
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
