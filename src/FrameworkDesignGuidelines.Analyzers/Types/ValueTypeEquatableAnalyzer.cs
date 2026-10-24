using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Types;

/// <summary>
/// FDG4032: an externally visible struct implements <c>IEquatable&lt;T&gt;</c> of itself. Record structs do so
/// already; ref structs are not checked.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueTypeEquatableAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG4032);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName("System.IEquatable`1") is { } equatable)
            {
                start.RegisterSymbolAction(symbol => AnalyzeType(symbol, equatable), SymbolKind.NamedType);
            }
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol equatable)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Struct || type.IsRefLikeType || !NamingSymbols.IsExternallyVisible(type)
            || type.AllInterfaces.Any(@interface => IsEquatableOf(@interface, equatable, type))
            || NamingSymbols.SourceLocation(type) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG4032, location, type.Name));
    }

    private static bool IsEquatableOf(INamedTypeSymbol @interface, INamedTypeSymbol equatable, INamedTypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, equatable)
            && SymbolEqualityComparer.Default.Equals(@interface.TypeArguments[0], type);
    }
}
