using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3051: an externally visible delegate type that no event in the compilation uses ends with <c>Callback</c>.
/// </summary>
/// <remarks>
/// A delegate whose name ends with <c>EventHandler</c> declares itself an event handler (FDG3050), so it is not
/// reported even when no event in this compilation uses it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DelegateCallbackSuffixAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3051);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var eventDelegates = new Lazy<HashSet<INamedTypeSymbol>>(
                () => NamingSymbols.EventDelegates(start.Compilation));
            start.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, eventDelegates.Value),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, HashSet<INamedTypeSymbol> eventDelegates)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } type
            || !NamingSymbols.IsExternallyVisible(type)
            || type.Name.EndsWith("Callback", StringComparison.Ordinal)
            || type.Name.EndsWith("EventHandler", StringComparison.Ordinal)
            || eventDelegates.Contains(type)
            || NamingSymbols.SourceLocation(type) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG3051, location, type.Name));
    }
}
