using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>Shared helpers for the chapter 5 analyzers, which compare the overloads that one type declares.</summary>
internal static class Overloads
{
    /// <summary>
    /// The overload families of <paramref name="type"/>: its externally visible ordinary methods grouped by name, and
    /// its externally visible instance constructors, each in declaration order. A family can have a single member.
    /// Implicit declarations and explicit interface implementations are left out.
    /// </summary>
    public static IEnumerable<ImmutableArray<IMethodSymbol>> Families(INamedTypeSymbol type)
    {
        return type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor
                && !method.IsImplicitlyDeclared
                && NamingSymbols.IsExternallyVisible(method))
            .GroupBy(method => method.Name, StringComparer.Ordinal)
            .Select(family => family.ToImmutableArray());
    }

    /// <summary>The name a diagnostic gives a family: the method name, or the type's name for constructors.</summary>
    public static string DisplayName(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Constructor
            ? method.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            : method.Name;
    }

    /// <summary>True when <paramref name="type"/> is <c>System.Threading.CancellationToken</c>.</summary>
    public static bool IsCancellationToken(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { Name: "CancellationToken", ContainingNamespace: { } ns }
            && ns.ToDisplayString() == "System.Threading";
    }

    /// <summary>True when both types are the same, ignoring nullable annotations.</summary>
    public static bool SameType(ITypeSymbol first, ITypeSymbol second)
    {
        return SymbolEqualityComparer.Default.Equals(first, second);
    }

    /// <summary>
    /// True when both parameters have the same type and the same <c>ref</c>, <c>out</c> or <c>in</c> modifier.
    /// </summary>
    public static bool SameShape(IParameterSymbol first, IParameterSymbol second)
    {
        return first.RefKind == second.RefKind && SameType(first.Type, second.Type);
    }

    /// <summary>
    /// The number of arguments a call must pass: parameters without a default value that are not a params array.
    /// </summary>
    public static int RequiredCount(IMethodSymbol method)
    {
        return method.Parameters.Count(parameter => !parameter.IsOptional && !parameter.IsParams);
    }

    /// <summary>True when the method has at least one parameter with a default value.</summary>
    public static bool HasDefaults(IMethodSymbol method)
    {
        return method.Parameters.Any(parameter => parameter.IsOptional);
    }

    /// <summary>
    /// The member of a pair to report: a member that overrides or implements another one takes its signature from
    /// it, so the other member is reported; otherwise <paramref name="later"/>. Null when both are inherited.
    /// </summary>
    public static IMethodSymbol? Target(IMethodSymbol earlier, IMethodSymbol later)
    {
        if (!NamingSymbols.IsInherited(later))
        {
            return later;
        }

        return NamingSymbols.IsInherited(earlier) ? null : earlier;
    }
}
