using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5004: in a family of overloads that extend one another, only the longest is virtual. A virtual or abstract
/// method of a class is reported when another overload in the same type starts with the same parameters (names,
/// types and modifiers) and adds more with the same accessibility. A public method that extends to a protected one is
/// not reported: callers cannot call the protected overload, so they are not overloads for them. Overrides follow
/// their base member and are not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VirtualOverloadAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5004);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class)
        {
            return;
        }

        foreach (ImmutableArray<IMethodSymbol> family in Overloads.Families(type))
        {
            foreach (IMethodSymbol method in family)
            {
                if ((method.IsVirtual || method.IsAbstract) && !method.IsOverride
                    && family.Any(longer => Extends(longer, method))
                    && NamingSymbols.SourceLocation(method) is { } location)
                {
                    context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5004, location, method.Name));
                }
            }
        }
    }

    private static bool Extends(IMethodSymbol longer, IMethodSymbol shorter)
    {
        if (longer.Parameters.Length <= shorter.Parameters.Length
            || longer.DeclaredAccessibility != shorter.DeclaredAccessibility)
        {
            return false;
        }

        for (int i = 0; i < shorter.Parameters.Length; i++)
        {
            if (shorter.Parameters[i].Name != longer.Parameters[i].Name
                || !Overloads.SameShape(shorter.Parameters[i], longer.Parameters[i]))
            {
                return false;
            }
        }

        return true;
    }
}
