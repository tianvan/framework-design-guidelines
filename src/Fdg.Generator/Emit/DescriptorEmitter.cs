using System.Text;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Emit;

/// <summary>Writes <c>FdgDescriptors.g.cs</c>: one <c>DiagnosticDescriptor</c> per rule checked by an fdg analyzer.</summary>
public static class DescriptorEmitter
{
    public static string Emit(GeneratorInput input)
    {
        string helpLinkBase = input.Meta.RepositoryUrl.TrimEnd('/') + "/blob/main/docs/rules/";
        var text = new StringBuilder();
        text.Append(Header.CSharp).Append('\n');
        text.Append("#nullable enable\n\n");
        text.Append("using Microsoft.CodeAnalysis;\n\n");
        text.Append("namespace FrameworkDesignGuidelines.Analyzers;\n\n");
        text.Append("internal static class FdgDescriptors\n{\n");

        IEnumerable<Rule> analyzed = input.Rules.Where(r => r.Mapping.Check.Kind == CheckKind.Fdg && r.Mapping.Analyzer is not null);
        text.Append(string.Join("\n", analyzed.Select(rule => Descriptor(rule, helpLinkBase))));
        text.Append("}\n");
        return text.ToString();
    }

    private static string Descriptor(Rule rule, string helpLinkBase)
    {
        AnalyzerInfo analyzer = rule.Mapping.Analyzer!;
        return
            $"    public static readonly DiagnosticDescriptor {rule.Id} = new DiagnosticDescriptor(\n" +
            $"        id: {Literal(rule.Id)},\n" +
            $"        title: {Literal(RuleText.Statement(rule))},\n" +
            $"        messageFormat: {Literal(analyzer.Message)},\n" +
            $"        category: {Literal("FrameworkDesignGuidelines." + analyzer.Category)},\n" +
            "        defaultSeverity: DiagnosticSeverity.Warning,\n" +
            "        isEnabledByDefault: false,\n" +
            $"        helpLinkUri: {Literal(helpLinkBase + rule.Id + ".md")});\n";
    }

    private static string Literal(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value)
        {
            text.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        }

        return text.Append('"').ToString();
    }
}
