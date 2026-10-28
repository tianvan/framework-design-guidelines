using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5034: an externally visible indexer has one parameter. Indexers that override or implement another member are
/// not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MultiParameterIndexerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5034);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var indexer = (IPropertySymbol)context.Symbol;
        if (indexer.IsIndexer && indexer.Parameters.Length > 1
            && NamingSymbols.IsExternallyVisible(indexer) && !NamingSymbols.IsInherited(indexer)
            && NamingSymbols.SourceLocation(indexer) is { } location)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG5034,
                location,
                indexer.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }
}
