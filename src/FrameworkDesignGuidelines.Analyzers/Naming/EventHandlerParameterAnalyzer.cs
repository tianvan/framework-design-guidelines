using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3075: an externally visible event handler delegate, one that an event in the compilation uses or whose name
/// ends with <c>EventHandler</c>, has exactly two parameters, named <c>sender</c> and <c>e</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventHandlerParameterAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3075);

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
        if (context.Symbol is not INamedTypeSymbol
                { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke } type
            || !NamingSymbols.IsExternallyVisible(type)
            || !(type.Name.EndsWith("EventHandler", StringComparison.Ordinal) || eventDelegates.Contains(type))
            || (invoke.Parameters is { Length: 2 } parameters
                && parameters[0].Name == "sender" && parameters[1].Name == "e")
            || NamingSymbols.SourceLocation(type) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG3075, location, type.Name));
    }
}
