using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5003: a parameter keeps its position in every overload, declared in the same type, that has a parameter with
/// the same name. <c>params</c> arrays and <c>out</c> parameters are left out, as the rule's body allows, and so are
/// <c>CancellationToken</c> parameters, which section 9.2 keeps last. Of two overloads that disagree, the one with
/// more parameters is reported (the later one on a tie), unless it overrides or implements another member. Overloads
/// with different numbers of type parameters are not compared, as a type argument can take the place of a parameter.
/// Exception constructors are not compared either: section 7.3 asks for the common constructors
/// <c>(string message)</c> and <c>(string message, Exception innerException)</c>, which fixes where those go.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OverloadParameterOrderAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5003);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        foreach (ImmutableArray<IMethodSymbol> family in Overloads.Families((INamedTypeSymbol)context.Symbol))
        {
            var reported = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            for (int i = 0; i < family.Length; i++)
            {
                for (int j = i + 1; j < family.Length; j++)
                {
                    if (Target(family[i], family[j]) is not { } target)
                    {
                        continue;
                    }

                    IMethodSymbol other =
                        SymbolEqualityComparer.Default.Equals(target, family[i]) ? family[j] : family[i];
                    foreach (IParameterSymbol parameter in target.Parameters.Where(IsCompared))
                    {
                        if (other.Parameters.FirstOrDefault(p => p.Name == parameter.Name && IsCompared(p)) is { } match
                            && match.Ordinal != parameter.Ordinal
                            && reported.Add(parameter)
                            && NamingSymbols.SourceLocation(parameter) is { } location)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                FdgDescriptors.FDG5003, location, parameter.Name, Overloads.DisplayName(target)));
                        }
                    }
                }
            }
        }
    }

    private static IMethodSymbol? Target(IMethodSymbol earlier, IMethodSymbol later)
    {
        if (earlier.Arity != later.Arity
            || (earlier.MethodKind == MethodKind.Constructor && IsException(earlier.ContainingType)))
        {
            return null;
        }

        bool earlierInherited = NamingSymbols.IsInherited(earlier);
        bool laterInherited = NamingSymbols.IsInherited(later);
        if (earlierInherited != laterInherited)
        {
            return earlierInherited ? later : earlier;
        }

        if (earlierInherited)
        {
            return null;
        }

        return earlier.Parameters.Length > later.Parameters.Length ? earlier : later;
    }

    private static bool IsCompared(IParameterSymbol parameter)
    {
        return !parameter.IsParams && parameter.RefKind != RefKind.Out
            && !Overloads.IsCancellationToken(parameter.Type);
    }

    private static bool IsException(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current is { Name: "Exception", ContainingNamespace: { } ns } && ns.ToDisplayString() == "System")
            {
                return true;
            }
        }

        return false;
    }
}
