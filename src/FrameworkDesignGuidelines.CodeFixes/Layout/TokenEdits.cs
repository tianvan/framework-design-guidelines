using FrameworkDesignGuidelines.Analyzers.Layout;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.CodeFixes.Layout;

/// <summary>
/// Collects trivia edits on the original tokens of a syntax tree and applies them in one replacement,
/// so that a token touched by several edits (for example a one-token statement) gets all of them.
/// </summary>
/// <remarks>
/// Only the edited gaps are formatted, as text spans, never the tokens around them: formatting a token also
/// reformats the gaps on both sides of it, which sibling fixes in the same FixAll batch may be editing (a
/// <c>try</c> starting an expanded block would get its own <c>{</c> moved by both fixes, leaving an empty line).
/// <see cref="ApplyAsync"/> formats with the document's own line ending and leaves no
/// <see cref="Formatter.Annotation"/>, so the code action's cleanup cannot insert
/// <see cref="Environment.NewLine"/> into a file that uses the other line ending.
/// </remarks>
internal sealed class TokenEdits
{
    /// <summary>Marks the token after each edited gap.</summary>
    private static readonly SyntaxAnnotation GapAnnotation = new("FrameworkDesignGuidelines.EditedGap");

    private readonly Dictionary<SyntaxToken, SyntaxToken> _replacements = new();
    private readonly SourceText _text;
    private readonly string _endOfLineText;
    private readonly SyntaxTrivia _endOfLine;

    public TokenEdits(SourceText text)
    {
        _text = text;
        _endOfLineText = DetectEndOfLine(text);
        _endOfLine = SyntaxFactory.EndOfLine(_endOfLineText);
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

        ForceBreakBetween(previous, next, indentation);
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
    /// Removes the whitespace and line breaks between two tokens, keeping any comments. When a single-line
    /// comment ends the gap, its line break stays and <paramref name="next"/> is indented by
    /// <paramref name="indentationAfterComment"/>.
    /// </summary>
    public void Join(SyntaxToken previous, SyntaxToken next, string indentationAfterComment)
    {
        SyntaxTriviaList trailing = TrimEnd(previous.TrailingTrivia);
        if (trailing.Count > 0 && trailing[trailing.Count - 1].IsKind(SyntaxKind.SingleLineCommentTrivia))
        {
            ForceBreakBetween(previous, next, indentationAfterComment);
            return;
        }

        Update(previous, token => token.WithTrailingTrivia(TrimEnd(token.TrailingTrivia)), annotate: false);
        Update(next, token => token.WithLeadingTrivia(TrimStart(token.LeadingTrivia)), annotate: true);
    }

    /// <summary>
    /// Moves every line of <paramref name="node"/> after its first by <paramref name="delta"/> columns, so that a
    /// multi-line item keeps its shape when its first token moves.
    /// </summary>
    public void ShiftContinuationLines(SyntaxNode node, int delta, bool useTabs)
    {
        if (delta == 0)
        {
            return;
        }

        SyntaxToken first = node.GetFirstToken();
        foreach (SyntaxToken token in node.DescendantTokens())
        {
            if (token == first)
            {
                continue;
            }

            bool atLineStart = EndsLine(token.GetPreviousToken().TrailingTrivia);
            if (atLineStart || token.LeadingTrivia.Any(IsLineTrivia))
            {
                Update(
                    token,
                    current => current.WithLeadingTrivia(Shift(current.LeadingTrivia, atLineStart, delta, useTabs)),
                    annotate: false);
            }
        }
    }

    /// <summary>
    /// Applies the edits, formats the edited gaps with the document's options and line ending, and removes the
    /// gap annotation again.
    /// </summary>
    public async Task<Document> ApplyAsync(Document document, SyntaxNode root, CancellationToken cancellationToken)
    {
        SyntaxNode edited = root.ReplaceTokens(_replacements.Keys, (original, _) => _replacements[original]);
        Document editedDocument = document.WithSyntaxRoot(edited);
        OptionSet options = (await editedDocument.GetOptionsAsync(cancellationToken).ConfigureAwait(false))
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, _endOfLineText);
        TextSpan[] gaps = edited.GetAnnotatedTokens(GapAnnotation)
            .Select(token => TextSpan.FromBounds(token.GetPreviousToken().Span.End, token.SpanStart))
            .ToArray();
        Document formatted = await Formatter.FormatAsync(editedDocument, gaps, options, cancellationToken)
            .ConfigureAwait(false);
        SyntaxNode? formattedRoot = await formatted.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (formattedRoot is null)
        {
            return formatted;
        }

        IEnumerable<SyntaxToken> annotated = formattedRoot.GetAnnotatedTokens(GapAnnotation);
        return formatted.WithSyntaxRoot(formattedRoot.ReplaceTokens(
            annotated,
            (_, rewritten) => rewritten.WithoutAnnotations(GapAnnotation)));
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

    /// <summary>True for trivia after which the next trivia or token starts a line.</summary>
    private static bool IsLineTrivia(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.EndOfLineTrivia) || trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia);
    }

    private static bool EndsLine(SyntaxTriviaList trivia)
    {
        return trivia.Count > 0 && IsLineTrivia(trivia[trivia.Count - 1]);
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

    private static SyntaxTriviaList TrimStart(SyntaxTriviaList trivia)
    {
        return SyntaxFactory.TriviaList(trivia.SkipWhile(IsWhitespace));
    }

    /// <summary>
    /// Indents the token's line and any comment lines in front of it; preprocessor directives stay where they are.
    /// </summary>
    private static SyntaxTriviaList Indent(SyntaxTriviaList trivia, string indentation)
    {
        SyntaxTrivia whitespace = SyntaxFactory.Whitespace(indentation);
        var result = new List<SyntaxTrivia>();
        bool atLineStart = true;
        foreach (SyntaxTrivia item in TrimStart(trivia))
        {
            if (atLineStart && item.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                result.Add(whitespace);
                atLineStart = false;
                continue;
            }

            if (atLineStart && !IsLineTrivia(item))
            {
                result.Add(whitespace);
            }

            result.Add(item);
            atLineStart = IsLineTrivia(item);
        }

        if (atLineStart)
        {
            result.Add(whitespace);
        }

        return SyntaxFactory.TriviaList(result);
    }

    private static SyntaxTriviaList Shift(SyntaxTriviaList trivia, bool atLineStart, int delta, bool useTabs)
    {
        var result = new List<SyntaxTrivia>();
        foreach (SyntaxTrivia item in trivia)
        {
            if (atLineStart && item.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                AddIndentation(result, LayoutText.GetWidth(item.ToString()) + delta, useTabs);
                atLineStart = false;
                continue;
            }

            if (atLineStart && !IsLineTrivia(item))
            {
                AddIndentation(result, delta, useTabs);
            }

            result.Add(item);
            atLineStart = IsLineTrivia(item);
        }

        if (atLineStart)
        {
            AddIndentation(result, delta, useTabs);
        }

        return SyntaxFactory.TriviaList(result);
    }

    private static void AddIndentation(List<SyntaxTrivia> trivia, int width, bool useTabs)
    {
        if (width > 0)
        {
            trivia.Add(SyntaxFactory.Whitespace(LayoutText.CreateIndentation(width, useTabs)));
        }
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
        _replacements[original] = annotate && !edited.HasAnnotation(GapAnnotation)
            ? edited.WithAdditionalAnnotations(GapAnnotation)
            : edited;
    }
}
