using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FrameworkDesignGuidelines.Analyzers.Exceptions;

/// <summary>Helpers shared by the exception analyzers of chapter 7.</summary>
internal static class ExceptionSymbols
{
    /// <summary>True when <paramref name="type"/> is <paramref name="baseType"/> or derives from it.</summary>
    public static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
        {
            return false;
        }

        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The innermost catch clause whose block contains <paramref name="node"/>, or null when there is none or when a
    /// lambda, anonymous method or local function, whose code runs later, comes first.
    /// </summary>
    public static CatchClauseSyntax? EnclosingCatch(SyntaxNode node)
    {
        for (SyntaxNode? current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax:
                    return null;
                case CatchClauseSyntax { Block: var block } catchClause when block.Span.Contains(node.Span):
                    return catchClause;
            }
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="node"/> is inside the when clause of a catch, with no lambda, anonymous method or
    /// local function in between.
    /// </summary>
    public static bool IsInFilter(SyntaxNode node)
    {
        for (SyntaxNode? current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax
                    or StatementSyntax:
                    return false;
                case CatchFilterClauseSyntax:
                    return true;
            }
        }

        return false;
    }

    /// <summary>The type of a catch clause's declaration, or null for a catch without one.</summary>
    public static ITypeSymbol? CaughtType(CatchClauseSyntax catchClause, SemanticModel model)
    {
        return catchClause.Declaration is { Type: var type } ? model.GetTypeInfo(type).Type : null;
    }
}
