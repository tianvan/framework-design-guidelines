using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// The default parameter rules of section 5.1.1, over the externally visible methods and constructors of a type:
/// FDG5010, a member with two or more default parameters has an overload without defaults and with no more
/// parameters than its required ones; FDG5011, interface members and virtual, abstract or override methods give no
/// parameter a default value except a <c>CancellationToken</c>, and an override of a member declared in a referenced
/// assembly is not reported for a default that repeats the overridden one; FDG5013, two overloads with defaults do
/// not have compatible required parameters; FDG5015, a parameter has the same default value in every overload.
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
        foreach (OverloadFamily family in Overloads.Families(type))
        {
            for (int i = 0; i < family.Length; i++)
            {
                AnalyzeVirtualDefaults(context, type, family[i]);
                if (!family.IsInherited(i))
                {
                    AnalyzeSimpleOverload(context, family.Methods, family[i]);
                }
            }

            var reportedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var reportedParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            for (int i = 0; i < family.Length; i++)
            {
                for (int j = i + 1; j < family.Length; j++)
                {
                    if (family.Target(i, j) is not { } index)
                    {
                        continue;
                    }

                    IMethodSymbol target = family[index];
                    IMethodSymbol other = index == i ? family[j] : family[i];
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

        bool referencedRoot = method.IsOverride && !IsRootInSource(method);
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.IsOptional && !Overloads.IsCancellationToken(parameter.Type)
                && !(referencedRoot && RepeatsOverriddenDefault(method, parameter))
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
        if (method.Parameters.Count(parameter => parameter.IsOptional) < 2)
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
    /// True when the method that <paramref name="method"/> overrides, followed back to the first declaration, is in
    /// source.
    /// </summary>
    private static bool IsRootInSource(IMethodSymbol method)
    {
        IMethodSymbol root = method;
        while (root.OverriddenMethod is { } overridden)
        {
            root = overridden;
        }

        return root.Locations.Any(location => location.IsInSource);
    }

    /// <summary>
    /// True when the overridden method gives <paramref name="parameter"/> the same explicit default. An override of a
    /// referenced member that repeats its default only follows a declaration the user cannot change; one that adds or
    /// changes a default creates the disagreement the rule is about.
    /// </summary>
    private static bool RepeatsOverriddenDefault(IMethodSymbol method, IParameterSymbol parameter)
    {
        return method.OverriddenMethod is { } overridden
            && parameter.Ordinal < overridden.Parameters.Length
            && overridden.Parameters[parameter.Ordinal] is { HasExplicitDefaultValue: true } original
            && parameter.HasExplicitDefaultValue
            && Equals(original.ExplicitDefaultValue, parameter.ExplicitDefaultValue);
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
