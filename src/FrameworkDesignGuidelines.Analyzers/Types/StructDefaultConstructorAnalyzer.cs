using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Types;

/// <summary>
/// FDG4026: a struct declares no parameterless instance constructor (C# 10 and later allow one, including a
/// parameterless primary constructor). The rule names no visibility, so every struct is checked.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StructDefaultConstructorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG4026);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Struct)
        {
            return;
        }

        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (!constructor.IsImplicitlyDeclared && constructor.Parameters.IsEmpty
                && NamingSymbols.SourceLocation(constructor) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG4026, location, type.Name));
            }
        }
    }
}
