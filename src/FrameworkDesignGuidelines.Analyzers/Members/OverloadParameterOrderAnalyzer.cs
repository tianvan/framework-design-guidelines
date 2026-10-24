using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5003: a parameter keeps its position in every overload, declared in the same type, that has a parameter with
/// the same name. <c>params</c> arrays and <c>out</c> parameters are left out, as the rule's body allows, and so are
/// <c>CancellationToken</c> parameters, which section 9.2 keeps last. Of two overloads that disagree, the one with
/// more parameters is reported (the later one on a tie), unless it overrides or implements another member. In exception
/// constructors, a leading <c>string message</c> and a trailing <c>Exception innerException</c> are left out before the
/// positions are compared: section 7.4 asks for the common constructors <c>(string message)</c> and
/// <c>(string message, Exception innerException)</c>, which fixes where those two go.
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
                    (int start, int end) = ComparedRange(target);
                    (int otherStart, int otherEnd) = ComparedRange(other);
                    for (int k = start; k < end; k++)
                    {
                        IParameterSymbol parameter = target.Parameters[k];
                        if (IsCompared(parameter)
                            && IndexOf(other, parameter.Name, otherStart, otherEnd) is { } match
                            && match - otherStart != k - start
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
    /// The range of parameters whose positions are compared. For a constructor of an exception type, a leading
    /// <c>string message</c> and a trailing <c>Exception innerException</c> (or <c>inner</c>) are left out, where the
    /// common constructors of section 7.4 put them, and positions are counted from after the message.
    /// </summary>
    private static (int Start, int End) ComparedRange(IMethodSymbol method)
    {
        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        if (method.MethodKind != MethodKind.Constructor || !IsException(method.ContainingType))
        {
            return (0, parameters.Length);
        }

        int start = parameters.Length > 0
            && parameters[0] is { Name: "message", Type.SpecialType: SpecialType.System_String } ? 1 : 0;
        int end = parameters.Length > start
            && parameters[parameters.Length - 1] is { Name: "innerException" or "inner" } last
            && last.Type is INamedTypeSymbol innerType && IsException(innerType)
            ? parameters.Length - 1
            : parameters.Length;
        return (start, end);
    }

    private static int? IndexOf(IMethodSymbol method, string name, int start, int end)
    {
        for (int k = start; k < end; k++)
        {
            if (method.Parameters[k].Name == name)
            {
                return IsCompared(method.Parameters[k]) ? k : null;
            }
        }

        return null;
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
