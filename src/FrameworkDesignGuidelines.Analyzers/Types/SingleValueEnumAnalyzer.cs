using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Types;

/// <summary>FDG4038: an externally visible enum does not have exactly one value.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingleValueEnumAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG4038);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Enum || !NamingSymbols.IsExternallyVisible(type)
            || type.GetMembers().OfType<IFieldSymbol>().Count(field => field.HasConstantValue) != 1
            || NamingSymbols.SourceLocation(type) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG4038, location, type.Name));
    }
}
