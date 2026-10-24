using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5003: a parameter keeps its position in every overload, declared in the same type, that has a parameter with
/// the same name. <c>params</c> arrays and <c>out</c> parameters are left out, as the rule's body allows, and so are
/// <c>CancellationToken</c> parameters, which section 9.2 keeps last. Of two overloads that disagree, the one with
/// more parameters is reported (the later one on a tie), unless it overrides or implements another member. A pair of
/// exception constructors is not compared when either starts with <c>string message</c>: section 7.4 asks for the
/// common constructors <c>(string message)</c> and <c>(string message, Exception innerException)</c>, which fixes
/// where those parameters go.
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
        foreach (OverloadFamily family in Overloads.Families((INamedTypeSymbol)context.Symbol))
        {
            var reported = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            for (int i = 0; i < family.Length; i++)
            {
                for (int j = i + 1; j < family.Length; j++)
                {
                    if (Target(family, i, j) is not { } index)
                    {
                        continue;
                    }

                    IMethodSymbol target = family[index];
                    IMethodSymbol other = index == i ? family[j] : family[i];
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

    private static int? Target(OverloadFamily family, int earlier, int later)
    {
        if (IsCommonExceptionConstructor(family[earlier]) || IsCommonExceptionConstructor(family[later]))
        {
            return null;
        }

        bool earlierInherited = family.IsInherited(earlier);
        bool laterInherited = family.IsInherited(later);
        if (earlierInherited != laterInherited)
        {
            return earlierInherited ? later : earlier;
        }

        if (earlierInherited)
        {
            return null;
        }

        return family[earlier].Parameters.Length > family[later].Parameters.Length ? earlier : later;
    }

    /// <summary>
    /// True for a constructor of an exception type whose first parameter is <c>string message</c>, the start of the
    /// common constructors of section 7.4.
    /// </summary>
    private static bool IsCommonExceptionConstructor(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Constructor
            && method.Parameters.FirstOrDefault() is { Name: "message", Type.SpecialType: SpecialType.System_String }
            && IsException(method.ContainingType);
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
