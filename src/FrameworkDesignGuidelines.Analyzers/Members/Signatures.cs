using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// Shared helpers for the parameter and tuple analyzers of sections 5.8 and 5.9, which look at the signatures of the
/// externally visible members a compilation declares.
/// </summary>
internal static class Signatures
{
    /// <summary>
    /// True when <paramref name="symbol"/> declares its own externally visible signature: a method, constructor,
    /// operator, conversion, delegate (its Invoke method), property, indexer, field or event. Accessors, implicit
    /// declarations, and overrides and interface implementations, which take their signature from the member they
    /// override or implement, are left out.
    /// </summary>
    public static bool IsDeclared(ISymbol symbol)
    {
        bool hasSignature = symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } => true,
            IMethodSymbol method => !method.IsImplicitlyDeclared && method.MethodKind is MethodKind.Ordinary
                or MethodKind.Constructor or MethodKind.UserDefinedOperator or MethodKind.Conversion,
            IPropertySymbol or IFieldSymbol or IEventSymbol => !symbol.IsImplicitlyDeclared,
            _ => false,
        };
        return hasSignature && NamingSymbols.IsExternallyVisible(symbol) && !NamingSymbols.IsInherited(symbol);
    }

    /// <summary>
    /// The member whose signature a symbol action looks at: the Invoke method of a delegate type, which gets no
    /// action of its own, and the symbol itself otherwise.
    /// </summary>
    public static ISymbol Member(ISymbol symbol)
    {
        return symbol is INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke }
            ? invoke
            : symbol;
    }

    /// <summary>The parameters of a method or an indexer; empty for other symbols.</summary>
    public static ImmutableArray<IParameterSymbol> Parameters(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => ImmutableArray<IParameterSymbol>.Empty,
        };
    }

    /// <summary>
    /// The name a diagnostic gives a member: the type's name for a constructor or a delegate, "operator" and its
    /// token for an operator, and the member name otherwise.
    /// </summary>
    public static string Name(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol method)
        {
            return symbol.Name;
        }

        switch (method.MethodKind)
        {
            case MethodKind.Constructor or MethodKind.DelegateInvoke:
                return method.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            case MethodKind.Conversion:
                return "operator " + method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            case MethodKind.UserDefinedOperator:
                string token = SyntaxFacts.GetText(SyntaxFacts.GetOperatorKind(method.MetadataName));
                return token.Length == 0 ? method.Name : "operator " + token;
            default:
                return method.Name;
        }
    }
}
