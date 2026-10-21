using Fdg.Generator.Rules;

namespace Fdg.Generator.Tests;

public sealed class GeneratorCliTests : IDisposable
{
    private readonly string _rulesDir;
    private readonly string _bookDir = Path.Combine(AppContext.BaseDirectory, "fixtures", "book");

    public GeneratorCliTests()
    {
        _rulesDir = Path.Combine(Path.GetTempPath(), "fdg-gen-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rulesDir);
        File.WriteAllText(
            Path.Combine(_rulesDir, "meta.yaml"),
            "repositoryUrl: https://example.test/repo\n" +
            "chapters:\n" +
            "  - { token: \"9\", file: sample.xhtml, rules: ch09-sample.yaml, title: Sample Chapter }\n");
    }

    public void Dispose()
    {
        Directory.Delete(_rulesDir, recursive: true);
    }

    private (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = GeneratorCli.Run(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private (int ExitCode, string Output, string Error) Extract()
    {
        return Run("extract", "--book", _bookDir, "--chapter", "9", "--rules", _rulesDir);
    }

    private (int ExitCode, string Output, string Error) Verify()
    {
        return Run("verify-source", "--book", _bookDir, "--rules", _rulesDir);
    }

    [Fact]
    public void Extract_writes_rule_skeleton()
    {
        var result = Extract();

        Assert.Equal(0, result.ExitCode);
        IReadOnlyList<Rule> rules = RuleLoader.LoadRules(Path.Combine(_rulesDir, "ch09-sample.yaml"));
        Assert.Equal(4, rules.Count);
        Assert.All(rules, r => Assert.Equal(RuleStatus.Draft, r.Mapping.Status));
    }

    [Fact]
    public void Extract_never_overwrites()
    {
        Extract();
        string path = Path.Combine(_rulesDir, "ch09-sample.yaml");
        File.AppendAllText(path, "# hand edit\n");

        var result = Extract();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("already exists; extraction never overwrites", result.Error);
        Assert.EndsWith("# hand edit\n", File.ReadAllText(path));
    }

    [Fact]
    public void Extract_rejects_unknown_chapter()
    {
        var result = Run("extract", "--book", _bookDir, "--chapter", "Z", "--rules", _rulesDir);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Z", result.Error);
    }

    [Fact]
    public void Verify_source_accepts_untouched_extraction()
    {
        Extract();

        var result = Verify();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("4 rules", result.Output);
    }

    [Fact]
    public void Verify_source_accepts_mapping_edits()
    {
        Extract();
        string path = Path.Combine(_rulesDir, "ch09-sample.yaml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("status: draft", "status: verified"));

        Assert.Equal(0, Verify().ExitCode);
    }

    [Fact]
    public void Verify_source_detects_edited_book_text()
    {
        Extract();
        string path = Path.Combine(_rulesDir, "ch09-sample.yaml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("keeping names short.", "keeping names tiny."));

        var result = Verify();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("FDG9003", result.Error);
        Assert.Contains("text", result.Error);
    }

    [Fact]
    public void Verify_source_detects_missing_rule()
    {
        Extract();
        string path = Path.Combine(_rulesDir, "ch09-sample.yaml");
        IReadOnlyList<Rule> rules = RuleLoader.LoadRules(path);
        File.WriteAllText(path, RuleYaml.Serialize(rules.Take(3)));

        var result = Verify();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("3", result.Error);
        Assert.Contains("4", result.Error);
    }

    [Fact]
    public void Loader_reads_chapters_listed_in_meta()
    {
        Extract();

        IReadOnlyList<RuleFile> files = RuleLoader.LoadAll(_rulesDir);

        RuleFile file = Assert.Single(files);
        Assert.Equal("9", file.Chapter);
        Assert.Equal("Sample Chapter", file.Title);
        Assert.Equal("sample.xhtml", file.SourceFile);
        Assert.Equal(4, file.Rules.Count);
    }
}
