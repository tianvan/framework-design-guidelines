using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3033: an externally visible type is not named exactly <c>Element</c>, <c>Node</c>, <c>Log</c> or
/// <c>Message</c>, the overly general names that the rule text lists. Qualified names such as <c>XmlNode</c> pass.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GeneralTypeNameAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] _generalNames = ["Element", "Node", "Log", "Message"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3033);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsImplicitlyDeclared || !_generalNames.Contains(type.Name, StringComparer.Ordinal)
            || !NamingSymbols.IsExternallyVisible(type)
            || NamingSymbols.SourceLocation(type) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG3033, location, type.Name));
    }
}
