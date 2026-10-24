using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Types;

/// <summary>
/// FDG4013: an externally visible abstract class declares no protected internal constructor. Public constructors are
/// left to CA1012, which the rule also maps.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbstractConstructorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG4013);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || !type.IsAbstract || !NamingSymbols.IsExternallyVisible(type))
        {
            return;
        }

        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (!constructor.IsImplicitlyDeclared
                && constructor.DeclaredAccessibility == Accessibility.ProtectedOrInternal
                && NamingSymbols.SourceLocation(constructor) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG4013, location, type.Name));
            }
        }
    }
}
