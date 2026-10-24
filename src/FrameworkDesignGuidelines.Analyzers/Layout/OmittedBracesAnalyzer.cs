using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.Analyzers.Layout;

/// <summary>
/// FDGA004: the embedded statement of <c>if</c>, <c>else</c>, <c>for</c>, <c>foreach</c>, <c>while</c>, <c>do</c>,
/// <c>using</c>, <c>lock</c> and <c>fixed</c> must be a block, as IDE0011 reports it, except in an argument
/// validation preamble (FDGA005).
/// </summary>
/// <remarks>
/// Like IDE0011, an <c>else if</c> is not itself reported (its <c>if</c> is), nor a <c>using</c>, <c>lock</c> or
/// <c>fixed</c> statement whose embedded statement is another statement of the same kind.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OmittedBracesAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDGA004);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(
            AnalyzeOwner,
            SyntaxKind.IfStatement,
            SyntaxKind.ElseClause,
            SyntaxKind.ForStatement,
            SyntaxKind.ForEachStatement,
            SyntaxKind.ForEachVariableStatement,
            SyntaxKind.WhileStatement,
            SyntaxKind.DoStatement,
            SyntaxKind.UsingStatement,
            SyntaxKind.LockStatement,
            SyntaxKind.FixedStatement);
    }

    /// <summary>
    /// Finds the keyword of <paramref name="owner"/> and its embedded statement, and tells whether that statement
    /// is reported: it is not a block, and none of the exceptions applies.
    /// </summary>
    internal static bool IsReported(SyntaxNode owner, out SyntaxToken keyword, out StatementSyntax? statement)
    {
        (keyword, statement) = owner switch
        {
            IfStatementSyntax node => (node.IfKeyword, node.Statement),
            ElseClauseSyntax node => (node.ElseKeyword, node.Statement),
            ForStatementSyntax node => (node.ForKeyword, node.Statement),
            CommonForEachStatementSyntax node => (node.ForEachKeyword, node.Statement),
            WhileStatementSyntax node => (node.WhileKeyword, node.Statement),
            DoStatementSyntax node => (node.DoKeyword, node.Statement),
            UsingStatementSyntax node => (node.UsingKeyword, node.Statement),
            LockStatementSyntax node => (node.LockKeyword, node.Statement),
            FixedStatementSyntax node => (node.FixedKeyword, node.Statement),
            _ => (default(SyntaxToken), (StatementSyntax?)null),
        };

        if (statement is null || statement.IsMissing || keyword.IsMissing || statement is BlockSyntax)
        {
            return false;
        }

        bool isElseIf = owner is ElseClauseSyntax && statement is IfStatementSyntax;
        bool isStacked = owner is UsingStatementSyntax or LockStatementSyntax or FixedStatementSyntax
            && statement.RawKind == owner.RawKind;
        return !isElseIf && !isStacked && !IsInPreamble(owner);
    }

    /// <summary>
    /// True for an <c>if</c>...<c>throw</c> statement without <c>else</c> that belongs to the run of such statements
    /// at the start of a method, constructor, accessor, operator, local function or lambda body. A blank line ends
    /// the run, as the book says: once you add a blank line, you have left the preamble.
    /// </summary>
    private static bool IsInPreamble(SyntaxNode owner)
    {
        if (owner is not IfStatementSyntax statement || !IsValidation(statement)
            || statement.Parent is not BlockSyntax body || !IsMemberBody(body))
        {
            return false;
        }

        for (int i = 0; i < body.Statements.Count; i++)
        {
            StatementSyntax earlier = body.Statements[i];
            if (i > 0 && FollowsBlankLine(earlier))
            {
                return false;
            }

            if (earlier == statement)
            {
                return true;
            }

            if (earlier is not IfStatementSyntax validation || !IsValidation(validation))
            {
                return false;
            }
        }

        return false;
    }

    private static bool FollowsBlankLine(StatementSyntax statement)
    {
        SourceText text = statement.SyntaxTree.GetText();
        int previousLine = text.Lines.GetLineFromPosition(statement.GetFirstToken().GetPreviousToken().Span.End).LineNumber;
        int line = text.Lines.GetLineFromPosition(statement.SpanStart).LineNumber;
        for (int number = previousLine + 1; number < line; number++)
        {
            if (string.IsNullOrWhiteSpace(text.Lines[number].ToString()))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An <c>if</c> without <c>else</c> that only throws, with or without braces.</summary>
    private static bool IsValidation(IfStatementSyntax statement)
    {
        return statement.Else is null
            && (statement.Statement is ThrowStatementSyntax
                || statement.Statement is BlockSyntax { Statements: { Count: 1 } statements }
                    && statements[0] is ThrowStatementSyntax);
    }

    private static bool IsMemberBody(BlockSyntax body)
    {
        return body.Parent is MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or OperatorDeclarationSyntax
            or ConversionOperatorDeclarationSyntax
            or AccessorDeclarationSyntax
            or LocalFunctionStatementSyntax
            or AnonymousFunctionExpressionSyntax;
    }

    private static void AnalyzeOwner(SyntaxNodeAnalysisContext context)
    {
        if (IsReported(context.Node, out SyntaxToken keyword, out _))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDGA004, keyword.GetLocation()));
        }
    }
}
