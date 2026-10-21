using Fdg.Generator.Book;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Tests;

public class RuleYamlTests
{
    private static IReadOnlyList<Rule> SampleRules()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "book", "sample.xhtml");
        return BookParser.Parse(path, "9").Rules;
    }

    [Fact]
    public void Serializes_skeleton_fields_in_documented_form()
    {
        string yaml = RuleYaml.Serialize(SampleRules());

        Assert.Contains("- id: FDG9001\n", yaml);
        Assert.Contains("  strength: DO\n", yaml);
        Assert.Contains("  strength: DO NOT\n", yaml);
        Assert.Contains("  strength: CONSIDER\n", yaml);
        Assert.Contains("  context: null\n", yaml);
        Assert.Contains("  context: System.Attribute\n", yaml);
        Assert.Contains("  anchor: sec9_1_1\n", yaml);
        Assert.Contains("  status: draft\n", yaml);
        Assert.Contains("  style: false\n", yaml);
        Assert.Contains("    kind: judgment\n", yaml);
        Assert.Contains("    diagnostics: []\n", yaml);
        Assert.Contains("    coverage: none\n", yaml);
        Assert.Contains("  autofix: no\n", yaml);
        Assert.Contains("    library: warning\n", yaml);
        Assert.Contains("    library: suggestion\n", yaml);
        Assert.Contains("  config: []\n", yaml);
        Assert.Contains("  analyzer: null\n", yaml);
        Assert.DoesNotContain('\r', yaml);
    }

    [Fact]
    public void Multi_line_text_uses_literal_blocks()
    {
        string yaml = RuleYaml.Serialize(SampleRules());

        Assert.Contains("text: |-\n", yaml);
        Assert.Contains("      public class TextColor\n", yaml);
    }

    [Fact]
    public void Round_trips_through_yaml()
    {
        IReadOnlyList<Rule> original = SampleRules();

        string yaml = RuleYaml.Serialize(original);
        IReadOnlyList<Rule> loaded = RuleYaml.Deserialize(yaml);

        Assert.Equal(original.Count, loaded.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Id, loaded[i].Id);
            Assert.Equal(original[i].Strength, loaded[i].Strength);
            Assert.Equal(original[i].Text, loaded[i].Text);
            Assert.Equal(original[i].Context, loaded[i].Context);
            Assert.Equal(original[i].Section, loaded[i].Section);
            Assert.Equal(original[i].Anchor, loaded[i].Anchor);
            Assert.Equal(original[i].Body, loaded[i].Body);
            Assert.Equal(original[i].Mapping.Severity, loaded[i].Mapping.Severity);
            Assert.Equal(original[i].Mapping.Autofix, loaded[i].Mapping.Autofix);
        }

        Assert.Equal(yaml, RuleYaml.Serialize(loaded));
    }
}
