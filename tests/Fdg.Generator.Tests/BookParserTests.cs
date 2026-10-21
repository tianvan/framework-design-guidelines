using Fdg.Generator.Book;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Tests;

public class BookParserTests
{
    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "book", name);
    }

    private static BookChapter ParseSample()
    {
        return BookParser.Parse(Fixture("sample.xhtml"), "9");
    }

    [Fact]
    public void Parses_all_icon_items_in_book_order()
    {
        BookChapter chapter = ParseSample();

        Assert.Equal(new[] { "FDG9001", "FDG9002", "FDG9003", "FDG9004" }, chapter.Rules.Select(r => r.Id));
        Assert.Equal(4, chapter.IconCount);
        Assert.Equal(
            new[] { Strength.Do, Strength.DoNot, Strength.Consider, Strength.Do },
            chapter.Rules.Select(r => r.Strength));
    }

    [Fact]
    public void Strength_comes_from_strong_and_must_match_icon()
    {
        BookFormatException exception = Assert.Throws<BookFormatException>(
            () => BookParser.Parse(Fixture("mismatch.xhtml"), "9"));

        Assert.Contains("FDG9002", exception.Message);
        Assert.Contains("cross", exception.Message);
    }

    [Fact]
    public void Text_excludes_strength_and_icon()
    {
        Rule rule = ParseSample().Rules[0];

        Assert.Equal("use PascalCasing for the names of public types and members.", rule.Text);
    }

    [Fact]
    public void Code_blocks_normalize_glyph_zero_and_carriage_returns()
    {
        Rule rule = ParseSample().Rules[0];

        Block code = Assert.Single(rule.Body, b => b.Kind == BlockKind.Code);
        Assert.DoesNotContain('ʘ', code.Text);
        Assert.DoesNotContain('\r', code.Text);
        Assert.Contains("MinValue = 0;", code.Text);
        Assert.All(code.Text.Split('\n'), line => Assert.Equal(line.TrimEnd(), line));
    }

    [Fact]
    public void Codelink_paragraphs_are_dropped()
    {
        BookChapter chapter = ParseSample();

        Assert.All(
            chapter.Rules.SelectMany(r => r.Body),
            block => Assert.DoesNotContain("Click here to view code image", block.Text));
    }

    [Fact]
    public void Sidebar_becomes_annotation_with_author()
    {
        Rule rule = ParseSample().Rules[0];

        Block annotation = Assert.Single(rule.Body, b => b.Kind == BlockKind.Annotation);
        Assert.Equal("Krzysztof Cwalina", annotation.Author);
        Assert.DoesNotContain("Krzysztof Cwalina", annotation.Text);
        Assert.Equal(
            "An annotation written by hand for the fixture.\n\n```\nint annotated = 0;\n```\n\nA second annotation paragraph.",
            annotation.Text);
    }

    [Fact]
    public void Body_stops_at_next_guideline_or_heading()
    {
        BookChapter chapter = ParseSample();

        Rule first = chapter.Rules[0];
        Assert.Equal(
            new[] { BlockKind.Paragraph, BlockKind.Code, BlockKind.Annotation },
            first.Body.Select(b => b.Kind));
        Assert.DoesNotContain(first.Body, b => b.Text.Contains("underscores"));

        Rule third = chapter.Rules[2];
        Assert.DoesNotContain(third.Body, b => b.Text.Contains("Prose after the heading"));
    }

    [Fact]
    public void Lists_become_markdown_bullets()
    {
        Rule rule = ParseSample().Rules[1];

        Block list = Assert.Single(rule.Body, b => b.Kind == BlockKind.List);
        Assert.Equal("- leading underscores\n- embedded underscores, such as `my_value`", list.Text);
    }

    [Fact]
    public void Tables_in_a_body_become_markdown_tables()
    {
        Rule rule = ParseSample().Rules[2];

        Block table = Assert.Single(rule.Body, b => b.Kind == BlockKind.Table);
        Assert.Equal(
            "**Table 9-1: Sample Casing**\n\n| Identifier | Casing |\n| --- | --- |\n| Type | Pascal |\n| Parameter | camel |",
            table.Text);
    }

    [Fact]
    public void Table_items_get_row_context_and_empty_body()
    {
        Rule rule = ParseSample().Rules[3];

        Assert.Equal("System.Attribute", rule.Context);
        Assert.Equal("add the suffix “Attribute” to custom attribute classes.", rule.Text);
        Assert.Empty(rule.Body);
        Assert.All(ParseSample().Rules.Take(3), r => Assert.Null(r.Context));
    }

    [Fact]
    public void Prose_after_a_guideline_table_belongs_to_the_guideline_that_introduces_it()
    {
        BookChapter chapter = BookParser.Parse(Fixture("table-trailer.xhtml"), "9");

        Assert.Equal(new[] { "FDG9001", "FDG9002", "FDG9003", "FDG9004" }, chapter.Rules.Select(r => r.Id));
        Assert.Equal(
            new[] { "These suffixes apply to the whole hierarchy.", "public class ElementStream : Object { ... }" },
            chapter.Rules[0].Body.Select(b => b.Text));
        Assert.Empty(chapter.Rules[1].Body);
        Assert.Empty(chapter.Rules[2].Body);
        Assert.Empty(chapter.Rules[3].Body);
    }

    [Fact]
    public void Inline_code_becomes_backticks()
    {
        Rule rule = ParseSample().Rules[0];

        Block paragraph = Assert.Single(rule.Body, b => b.Kind == BlockKind.Paragraph);
        Assert.Equal(
            "For example, use `TextColor` rather than `Textcolor`. Single words, such as `Button`, simply have initial capitals.",
            paragraph.Text);
    }

    [Fact]
    public void Section_and_anchor_track_nearest_heading()
    {
        BookChapter chapter = ParseSample();

        Assert.Equal("9.1.1 Sample Capitalization Rules", chapter.Rules[0].Section);
        Assert.Equal("sec9_1_1", chapter.Rules[0].Anchor);
        Assert.Equal("9.1.2 Sample Type Guidelines", chapter.Rules[3].Section);
        Assert.Equal("sec9_1_2", chapter.Rules[3].Anchor);
    }

    [Fact]
    public void Extracted_rules_carry_default_mapping()
    {
        BookChapter chapter = ParseSample();

        RuleMapping doMapping = chapter.Rules[0].Mapping;
        Assert.Equal(RuleStatus.Draft, doMapping.Status);
        Assert.False(doMapping.Style);
        Assert.Equal(CheckKind.Judgment, doMapping.Check.Kind);
        Assert.Empty(doMapping.Check.Diagnostics);
        Assert.Equal(CoverageLevel.None, doMapping.Check.Coverage);
        Assert.Equal(AutofixKind.No, doMapping.Autofix);
        Assert.Equal(new ProfileSeverity(SeverityLevel.Warning, SeverityLevel.Warning, SeverityLevel.Warning), doMapping.Severity);

        RuleMapping considerMapping = chapter.Rules[2].Mapping;
        Assert.Equal(
            new ProfileSeverity(SeverityLevel.Suggestion, SeverityLevel.Suggestion, SeverityLevel.Suggestion),
            considerMapping.Severity);
    }

    [Fact]
    public void Appendix_a_rules_are_style_conventions()
    {
        BookChapter chapter = BookParser.Parse(Fixture("sample.xhtml"), "A");

        Assert.Equal("FDGA001", chapter.Rules[0].Id);
        Assert.All(chapter.Rules, r => Assert.True(r.Mapping.Style));
    }
}
