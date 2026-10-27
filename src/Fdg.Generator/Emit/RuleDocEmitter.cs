using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Writes <c>docs/rules/&lt;ID&gt;.md</c>, the page each diagnostic's help link points to.</summary>
public static class RuleDocEmitter
{
    public static IReadOnlyList<GeneratedFile> Emit(GeneratorInput input)
    {
        return input.Rules.Select(rule => new GeneratedFile($"docs/rules/{rule.Id}.md", Page(rule))).ToList();
    }

    private static string Page(Rule rule)
    {
        RuleMapping mapping = rule.Mapping;
        var text = new StringBuilder();
        text.Append(Header.Markdown).Append("\n\n");
        text.Append($"# {rule.Id}\n\n");
        text.Append($"> {RuleText.Statement(rule)}\n\n");
        text.Append($"Framework Design Guidelines, 3rd edition, section {RuleText.EscapeMarkdown(rule.Section)}.");
        if (rule.Context is not null)
        {
            text.Append(rule.Strength.IsVerdict() ? $" Can cause: {rule.Context}." : $" Applies to: {rule.Context}.");
        }

        text.Append("\n\n");
        if (mapping.Style)
        {
            text.Append("This is a coding style convention. The book treats the appendix A conventions as suggestions, so you can override it.\n\n");
        }

        text.Append("## How it is checked\n\n");
        text.Append($"- Check: {RuleYaml.EnumText(mapping.Check.Kind)}");
        if (mapping.Check.Kind is CheckKind.Sdk or CheckKind.Fdg)
        {
            text.Append($" ({RuleYaml.EnumText(mapping.Check.Coverage)} coverage)");
        }

        text.Append('\n');
        if (mapping.Check.Diagnostics.Count > 0)
        {
            text.Append($"- Diagnostics: {string.Join(", ", mapping.Check.Diagnostics)}\n");
        }

        text.Append($"- Automatic fix: {AutofixText(mapping.Autofix)}\n");
        text.Append("- Severity: ");
        text.Append(string.Join(", ", ProfileFile.Names.Select(p => $"{p} {RuleText.Level(RuleText.Severity(rule, p))}")));
        text.Append('\n');
        string? tag = RuleText.LanguageTag(rule);
        if (tag is not null)
        {
            text.Append($"- Language: {tag}");
            text.Append(mapping.Alternative is null ? "\n" : $" (before that: {mapping.Alternative})\n");
        }

        text.Append($"- Status: {RuleYaml.EnumText(mapping.Status)}\n");
        if (mapping.Analyzer is not null)
        {
            text.Append($"\nMessage: {mapping.Analyzer.Message}\n");
        }

        if (mapping.Notes is not null)
        {
            text.Append($"\n{mapping.Notes}\n");
        }

        if (mapping.Config.Count > 0)
        {
            text.Append("\n## Options\n\n```ini\n");
            text.Append(string.Join("\n", mapping.Config)).Append("\n```\n");
        }

        if (rule.Body.Count > 0)
        {
            text.Append("\n## From the book\n\n");
            text.Append(RuleText.RenderBody(rule.Body)).Append('\n');
        }

        return text.ToString();
    }

    private static string AutofixText(AutofixKind autofix)
    {
        return autofix switch
        {
            AutofixKind.Yes => "yes",
            AutofixKind.No => "no",
            _ => "not applied automatically (the fix can break callers)",
        };
    }
}
