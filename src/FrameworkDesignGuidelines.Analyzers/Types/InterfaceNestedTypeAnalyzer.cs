using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Types;

/// <summary>FDG4056: no externally visible type is declared inside an interface (C# 8 and later allow it).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InterfaceNestedTypeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG4056);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.ContainingType is not { TypeKind: TypeKind.Interface } @interface
            || !NamingSymbols.IsExternallyVisible(type)
            || NamingSymbols.SourceLocation(type) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG4056, location, type.Name, @interface.Name));
    }
}
