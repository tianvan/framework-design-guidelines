using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// The default parameter rules of section 5.1.1, over the externally visible methods and constructors of a type:
/// FDG5010, a member with two or more default parameters has an overload without defaults and with no more
/// parameters than its required ones; FDG5011, interface members and virtual, abstract or override methods give no
/// parameter a default value except a <c>CancellationToken</c>; FDG5013, two overloads with defaults do not have
/// compatible required parameters; FDG5015, a parameter has the same default value in every overload.
/// Members that override or implement another one are reported by FDG5011 only.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultParameterAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG5010, FdgDescriptors.FDG5011, FdgDescriptors.FDG5013, FdgDescriptors.FDG5015);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        foreach (ImmutableArray<IMethodSymbol> family in Overloads.Families(type))
        {
            foreach (IMethodSymbol method in family)
            {
                AnalyzeVirtualDefaults(context, type, method);
                AnalyzeSimpleOverload(context, family, method);
            }

            var reportedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var reportedParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            for (int i = 0; i < family.Length; i++)
            {
                for (int j = i + 1; j < family.Length; j++)
                {
                    if (Overloads.Target(family[i], family[j]) is not { } target)
                    {
                        continue;
                    }

                    IMethodSymbol other =
                        SymbolEqualityComparer.Default.Equals(target, family[i]) ? family[j] : family[i];
                    AnalyzeCompatibleDefaults(context, target, other, reportedMethods);
                    AnalyzeDifferentDefaults(context, target, other, reportedParameters);
                }
            }
        }
    }

    private static void AnalyzeVirtualDefaults(
        SymbolAnalysisContext context, INamedTypeSymbol type, IMethodSymbol method)
    {
        bool isVirtual = type.TypeKind switch
        {
            TypeKind.Interface => method.IsAbstract || method.IsVirtual,
            TypeKind.Class => method.IsVirtual || method.IsAbstract || method.IsOverride,
            _ => false,
        };
        if (!isVirtual)
        {
            return;
        }

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.IsOptional && !Overloads.IsCancellationToken(parameter.Type)
                && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG5011, location, parameter.Name, Overloads.DisplayName(method)));
            }
        }
    }

    private static void AnalyzeSimpleOverload(
        SymbolAnalysisContext context, ImmutableArray<IMethodSymbol> family, IMethodSymbol method)
    {
        if (method.Parameters.Count(parameter => parameter.IsOptional) < 2 || NamingSymbols.IsInherited(method))
        {
            return;
        }

        int required = Overloads.RequiredCount(method);
        if (!family.Any(simple => !Overloads.HasDefaults(simple) && simple.Parameters.Length <= required)
            && NamingSymbols.SourceLocation(method) is { } location)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG5010, location, Overloads.DisplayName(method)));
        }
    }

    private static void AnalyzeCompatibleDefaults(
        SymbolAnalysisContext context, IMethodSymbol target, IMethodSymbol other, HashSet<IMethodSymbol> reported)
    {
        if (Overloads.HasDefaults(target) && Overloads.HasDefaults(other)
            && (CallMatches(target, other) || CallMatches(other, target))
            && reported.Add(target)
            && NamingSymbols.SourceLocation(target) is { } location)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG5013, location, Overloads.DisplayName(target)));
        }
    }

    private static void AnalyzeDifferentDefaults(
        SymbolAnalysisContext context, IMethodSymbol target, IMethodSymbol other, HashSet<IParameterSymbol> reported)
    {
        foreach (IParameterSymbol parameter in target.Parameters.Where(p => p.HasExplicitDefaultValue))
        {
            if (other.Parameters.FirstOrDefault(p => p.Name == parameter.Name)
                    is { HasExplicitDefaultValue: true } match
                && Overloads.SameType(match.Type, parameter.Type)
                && !Equals(match.ExplicitDefaultValue, parameter.ExplicitDefaultValue)
                && reported.Add(parameter)
                && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG5015, location, parameter.Name, Overloads.DisplayName(target)));
            }
        }
    }

    /// <summary>
    /// True when a call that passes only the required arguments of <paramref name="call"/> also matches
    /// <paramref name="callee"/>: the callee needs no more arguments, has room for them all, and takes the same types
    /// with the same modifiers at those positions.
    /// </summary>
    private static bool CallMatches(IMethodSymbol call, IMethodSymbol callee)
    {
        int count = Overloads.RequiredCount(call);
        if (Overloads.RequiredCount(callee) > count || callee.Parameters.Length < count)
        {
            return false;
        }

        for (int k = 0; k < count; k++)
        {
            if (!Overloads.SameShape(call.Parameters[k], callee.Parameters[k]))
            {
                return false;
            }
        }

        return true;
    }
}
