using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3013: the names that externally visible declarations choose contain only ASCII characters.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonAsciiNameAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3013);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeSymbol, NamingSymbols.DeclarationKinds);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        foreach ((ISymbol symbol, _) in NamingSymbols.DeclaredNames(context.Symbol))
        {
            if (symbol.Name.Any(c => c > '\u007f') && NamingSymbols.SourceLocation(symbol) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG3013, location, symbol.Name));
            }
        }
    }
}
