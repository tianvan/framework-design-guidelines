using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.CodeFixes.Layout;

/// <summary>
/// Collects trivia edits on the original tokens of a syntax tree and applies them in one replacement,
/// so that a token touched by several edits (for example a one-token statement) gets all of them.
/// The token after each edited gap carries <see cref="Formatter.Annotation"/>; the token before it does not,
/// because formatting that token would also reformat the gap in front of it, which a sibling fix in the same
/// FixAll batch may be editing.
/// </summary>
internal sealed class TokenEdits
{
    private readonly Dictionary<SyntaxToken, SyntaxToken> _replacements = new();
    private readonly SourceText _text;
    private readonly SyntaxTrivia _endOfLine;

    public TokenEdits(SourceText text)
    {
        _text = text;
        _endOfLine = SyntaxFactory.EndOfLine(DetectEndOfLine(text));
    }

    /// <summary>
    /// Ensures <paramref name="next"/> starts a new line indented by <paramref name="indentation"/>.
    /// Tokens already on different lines are left as they are.
    /// </summary>
    public void BreakBetween(SyntaxToken previous, SyntaxToken next, string indentation)
    {
        if (AreOnDifferentLines(previous, next))
        {
            return;
        }

        Update(previous, token => token.WithTrailingTrivia(TrimEnd(token.TrailingTrivia).Add(_endOfLine)), annotate: false);
        Update(next, token => token.WithLeadingTrivia(Indent(token.LeadingTrivia, indentation)), annotate: true);
    }

    /// <summary>
    /// Ends the line after <paramref name="previous"/> and indents <paramref name="next"/>, even when the two
    /// tokens are already on different lines.
    /// </summary>
    public void ForceBreakBetween(SyntaxToken previous, SyntaxToken next, string indentation)
    {
        Update(previous, token => token.WithTrailingTrivia(TrimEnd(token.TrailingTrivia).Add(_endOfLine)), annotate: false);
        Update(next, token => token.WithLeadingTrivia(Indent(token.LeadingTrivia, indentation)), annotate: true);
    }

    /// <summary>
    /// Removes the whitespace and line breaks between two tokens, keeping any comments; a line break that ends
    /// a single-line comment stays.
    /// </summary>
    public void Join(SyntaxToken previous, SyntaxToken next)
    {
        Update(previous, token => token.WithTrailingTrivia(EndSingleLineComment(TrimEnd(token.TrailingTrivia))), annotate: false);
        Update(next, token => token.WithLeadingTrivia(TrimStart(token.LeadingTrivia)), annotate: true);
    }

    public SyntaxNode Apply(SyntaxNode root)
    {
        return root.ReplaceTokens(_replacements.Keys, (original, _) => _replacements[original]);
    }

    private static string DetectEndOfLine(SourceText text)
    {
        if (text.Lines.Count > 1)
        {
            TextLine first = text.Lines[0];
            return text.ToString(TextSpan.FromBounds(first.End, first.EndIncludingLineBreak));
        }

        return "\n";
    }

    private static bool IsWhitespace(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia);
    }

    private static SyntaxTriviaList TrimEnd(SyntaxTriviaList trivia)
    {
        int count = trivia.Count;
        while (count > 0 && IsWhitespace(trivia[count - 1]))
        {
            count--;
        }

        return SyntaxFactory.TriviaList(trivia.Take(count));
    }

    private SyntaxTriviaList EndSingleLineComment(SyntaxTriviaList trivia)
    {
        return trivia.Count > 0 && trivia[trivia.Count - 1].IsKind(SyntaxKind.SingleLineCommentTrivia)
            ? trivia.Add(_endOfLine)
            : trivia;
    }

    private static SyntaxTriviaList TrimStart(SyntaxTriviaList trivia)
    {
        return SyntaxFactory.TriviaList(trivia.SkipWhile(IsWhitespace));
    }

    /// <summary>
    /// Replaces the indentation of the token's line and of any comment lines in front of it.
    /// </summary>
    private static SyntaxTriviaList Indent(SyntaxTriviaList trivia, string indentation)
    {
        SyntaxTrivia whitespace = SyntaxFactory.Whitespace(indentation);
        var result = new List<SyntaxTrivia> { whitespace };
        result.AddRange(TrimStart(trivia));
        int last = result.Count - 1;
        if (last > 0 && result[last].IsKind(SyntaxKind.WhitespaceTrivia) && result[last - 1].IsKind(SyntaxKind.EndOfLineTrivia))
        {
            result[last] = whitespace;
        }
        else if (last > 0 && result[last].IsKind(SyntaxKind.EndOfLineTrivia))
        {
            result.Add(whitespace);
        }

        return SyntaxFactory.TriviaList(result);
    }

    private bool AreOnDifferentLines(SyntaxToken previous, SyntaxToken next)
    {
        return _text.Lines.GetLineFromPosition(previous.Span.End).LineNumber
            != _text.Lines.GetLineFromPosition(next.SpanStart).LineNumber;
    }

    private void Update(SyntaxToken original, Func<SyntaxToken, SyntaxToken> edit, bool annotate)
    {
        SyntaxToken current = _replacements.TryGetValue(original, out SyntaxToken replaced) ? replaced : original;
        SyntaxToken edited = edit(current);
        _replacements[original] = annotate && !edited.HasAnnotation(Formatter.Annotation)
            ? edited.WithAdditionalAnnotations(Formatter.Annotation)
            : edited;
    }
}
