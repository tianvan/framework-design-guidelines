using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.Analyzers.Layout;

/// <summary>
/// FDGA023: a parameter or argument list that is exceedingly long must be chopped: a line break after the
/// opening parenthesis, one item per line indented one level from the line of the opening parenthesis, and the
/// closing parenthesis right after the last item. A list is exceedingly long when its line is longer than
/// <c>max_line_length</c>, with a tab reaching the next tab stop of <c>tab_width</c> (4 when not set), or when the
/// author wrapped it over several lines. A line that is long only because a conditional, <c>||</c>,
/// <c>&amp;&amp;</c> or <c>??</c> expression joins short calls does not make their lists long.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LongListWrappingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDGA023);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(
            AnalyzeList,
            SyntaxKind.ParameterList,
            SyntaxKind.BracketedParameterList,
            SyntaxKind.ArgumentList);
    }

    /// <summary>
    /// Recognizes the parameter lists of methods, constructors, delegates, local functions, operators and
    /// indexers, and the argument lists of invocations, object creations and constructor initializers, when
    /// they have at least two items.
    /// </summary>
    internal static bool TryGetList(SyntaxNode node, out ListShape shape)
    {
        shape = node switch
        {
            ParameterListSyntax list when list.Parent is MethodDeclarationSyntax
                or ConstructorDeclarationSyntax
                or DelegateDeclarationSyntax
                or LocalFunctionStatementSyntax
                or OperatorDeclarationSyntax
                or ConversionOperatorDeclarationSyntax
                => new ListShape(list.OpenParenToken, list.Parameters, list.CloseParenToken),
            BracketedParameterListSyntax list when list.Parent is IndexerDeclarationSyntax
                => new ListShape(list.OpenBracketToken, list.Parameters, list.CloseBracketToken),
            ArgumentListSyntax list when list.Parent is InvocationExpressionSyntax
                or BaseObjectCreationExpressionSyntax
                or ConstructorInitializerSyntax
                => new ListShape(list.OpenParenToken, list.Arguments, list.CloseParenToken),
            _ => default,
        };

        return shape.Items is { Count: >= 2 } && !shape.Open.IsMissing && !shape.Close.IsMissing;
    }

    /// <summary>
    /// True when the list is already chopped: the first item starts on a line after the opening parenthesis,
    /// every item starts its own line at one indentation level past the opening parenthesis's line (after any block
    /// comments, as in <c>/* name */ value</c>), and the closing parenthesis is on the line where the last item ends,
    /// or on the next line when a line comment ends the last item's line.
    /// </summary>
    internal static bool IsChopped(SourceText text, ListShape shape)
    {
        int expectedWidth = LayoutText.GetWidth(LayoutText.GetLineIndentation(text, shape.Open.SpanStart))
            + LayoutText.IndentUnit.Length;
        SyntaxToken previous = shape.Open;
        foreach (SyntaxNode item in shape.Items)
        {
            int start = item.SpanStart;
            if (LayoutText.GetLineNumber(text, previous.Span.End) == LayoutText.GetLineNumber(text, start))
            {
                return false;
            }

            TextLine line = text.Lines.GetLineFromPosition(start);
            string indentation = LayoutText.GetLineIndentation(text, start);
            if (!HasOnlyBlockCommentsBefore(item.GetFirstToken(), line)
                || LayoutText.GetWidth(indentation) != expectedWidth)
            {
                return false;
            }

            previous = item.GetLastToken().GetNextToken();
        }

        SyntaxToken last = shape.Items[shape.Items.Count - 1].GetLastToken();
        return LayoutText.GetLineNumber(text, last.Span.End) == LayoutText.GetLineNumber(text, shape.Close.SpanStart)
            || last.TrailingTrivia.Any(SyntaxKind.SingleLineCommentTrivia);
    }

    /// <summary>
    /// True when nothing but whitespace and block comments precedes <paramref name="token"/> on
    /// <paramref name="line"/>, given that the token before it ends on an earlier line.
    /// </summary>
    private static bool HasOnlyBlockCommentsBefore(SyntaxToken token, TextLine line)
    {
        foreach (SyntaxTrivia trivia in token.LeadingTrivia)
        {
            if (trivia.SpanStart >= line.Start
                && !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when the author wrapped the list: a line break falls between its items, that is before the first item
    /// or at a separator. Breaks only inside one item (a lambda body, an initializer) are not a wrap of the list.
    /// </summary>
    private static bool IsWrapped(SourceText text, ListShape shape)
    {
        foreach (SyntaxNode item in shape.Items)
        {
            SyntaxToken first = item.GetFirstToken();
            if (LayoutText.GetLineNumber(text, first.GetPreviousToken().Span.End) != LayoutText.GetLineNumber(text, first.SpanStart))
            {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeList(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetList(context.Node, out ListShape shape))
        {
            return;
        }

        SyntaxTree tree = context.Node.SyntaxTree;
        SourceText text = tree.GetText(context.CancellationToken);
        TextLine line = text.Lines.GetLineFromPosition(shape.Open.SpanStart);
        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree);
        int maxLength = LineLength.Get(options);
        int tabWidth = LineLength.GetTabWidth(options);
        if ((LineLength.GetWidth(text, line.Span, tabWidth) <= maxLength && !IsWrapped(text, shape))
            || IsChopped(text, shape)
            || IsExemptThroughOperator(text, context.Node, line, maxLength, tabWidth)
            || HasEarlierListOnLine(tree.GetRoot(context.CancellationToken), text, shape, line, maxLength, tabWidth))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDGA023, shape.Open.GetLocation()));
    }

    /// <summary>
    /// True when the list is not wrapped and its line is within <paramref name="maxLength"/> once the outermost
    /// enclosing conditional, <c>||</c>, <c>&amp;&amp;</c> or <c>??</c> expression on the line is reduced to the
    /// list's own invocation, including the fluent chain it is part of. The operator then makes the line long, not
    /// the invocation. Widths count a tab up to the next multiple of <paramref name="tabWidth"/>.
    /// </summary>
    private static bool IsLongOnlyThroughOperator(
        SourceText text,
        SyntaxNode list,
        ListShape shape,
        TextLine line,
        int maxLength,
        int tabWidth)
    {
        if (list is not ArgumentListSyntax || list.Parent is not ExpressionSyntax || IsWrapped(text, shape))
        {
            return false;
        }

        SyntaxNode chain = list.Parent;
        while (chain.Parent is SyntaxNode parent && IsChainLink(parent, chain))
        {
            chain = parent;
        }

        SyntaxNode? operatorExpression = null;
        for (SyntaxNode? node = chain.Parent;
            node is ExpressionSyntax and not AnonymousFunctionExpressionSyntax;
            node = node.Parent)
        {
            if (node.IsKind(SyntaxKind.ConditionalExpression)
                || node.IsKind(SyntaxKind.LogicalOrExpression)
                || node.IsKind(SyntaxKind.LogicalAndExpression)
                || node.IsKind(SyntaxKind.CoalesceExpression))
            {
                operatorExpression = node;
            }
        }

        return operatorExpression is not null
            && GetReducedWidth(text, line, operatorExpression, chain, tabWidth) <= maxLength;
    }

    /// <summary>
    /// Returns the width of <paramref name="line"/> once the part of <paramref name="operatorExpression"/> on it is
    /// replaced by the part of <paramref name="chain"/>, which the expression contains.
    /// </summary>
    private static int GetReducedWidth(
        SourceText text,
        TextLine line,
        SyntaxNode operatorExpression,
        SyntaxNode chain,
        int tabWidth)
    {
        TextSpan operatorPart = line.Span.Intersection(operatorExpression.Span) ?? new TextSpan(line.End, 0);
        TextSpan chainPart = line.Span.Intersection(chain.Span) ?? new TextSpan(operatorPart.Start, 0);
        int column = LineLength.Advance(text, TextSpan.FromBounds(line.Start, operatorPart.Start), 0, tabWidth);
        column = LineLength.Advance(text, chainPart, column, tabWidth);
        return LineLength.Advance(text, TextSpan.FromBounds(operatorPart.End, line.End), column, tabWidth);
    }

    /// <summary>
    /// True when <paramref name="list"/>, or a list enclosing it that opens on the same line, is long only through an
    /// operator: a list nested in the arguments of such a call is no longer than the call itself.
    /// </summary>
    private static bool IsExemptThroughOperator(
        SourceText text,
        SyntaxNode list,
        TextLine line,
        int maxLength,
        int tabWidth)
    {
        for (SyntaxNode? node = list; node is not null && node.SpanStart >= line.Start; node = node.Parent)
        {
            if (node is ArgumentListSyntax
                && TryGetList(node, out ListShape shape)
                && shape.Open.SpanStart >= line.Start
                && IsLongOnlyThroughOperator(text, node, shape, line, maxLength, tabWidth))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="parent"/> continues the fluent chain that ends at <paramref name="node"/>.</summary>
    private static bool IsChainLink(SyntaxNode parent, SyntaxNode node)
    {
        return parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression == node,
            InvocationExpressionSyntax invocation => invocation.Expression == node,
            ElementAccessExpressionSyntax access => access.Expression == node,
            ConditionalAccessExpressionSyntax => true,
            PostfixUnaryExpressionSyntax or AwaitExpressionSyntax => true,
            _ => false,
        };
    }

    /// <summary>
    /// Only the leftmost list opening on a line is reported: the one enclosing the others, or the first link of a
    /// fluent chain. The rest are left for the next run, which then indents them from their new line. A list that
    /// is long only through an operator does not hide the lists after it.
    /// </summary>
    private static bool HasEarlierListOnLine(
        SyntaxNode root,
        SourceText text,
        ListShape shape,
        TextLine line,
        int maxLength,
        int tabWidth)
    {
        for (SyntaxToken token = root.FindToken(line.Start);
            token.SpanStart < shape.Open.SpanStart && !token.IsKind(SyntaxKind.None);
            token = token.GetNextToken())
        {
            if (token.SpanStart >= line.Start
                && (token.IsKind(SyntaxKind.OpenParenToken) || token.IsKind(SyntaxKind.OpenBracketToken))
                && token.Parent is SyntaxNode list
                && TryGetList(list, out ListShape earlier)
                && earlier.Open == token
                && !IsExemptThroughOperator(text, list, line, maxLength, tabWidth))
            {
                return true;
            }
        }

        return false;
    }

    internal readonly struct ListShape
    {
        public ListShape(SyntaxToken open, IReadOnlyList<SyntaxNode> items, SyntaxToken close)
        {
            Open = open;
            Items = items;
            Close = close;
        }

        public SyntaxToken Open { get; }

        public IReadOnlyList<SyntaxNode> Items { get; }

        public SyntaxToken Close { get; }
    }
}
