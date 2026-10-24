using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Book;

/// <summary>Turns book markup into text and <see cref="Block"/> values.</summary>
public static partial class BlockRenderer
{
    // Stands for a <br> until whitespace is collapsed, so that the break can absorb the whitespace around it.
    private const char LineBreak = '\uE000';

    [GeneratedRegex(" ?\uE000 ?")]
    private static partial Regex LineBreakWithSpaces();

    /// <summary>
    /// The text of a node as Markdown, with whitespace collapsed: code in backticks, emphasis in <c>*</c>,
    /// strong in <c>**</c>, links to the web as <c>[text](href)</c> and line breaks as <c>&lt;br&gt;</c>.
    /// Links within the book keep only their text.
    /// </summary>
    public static string Inline(INode node)
    {
        var builder = new StringBuilder();
        AppendInline(node, builder);
        return Finish(builder.ToString());
    }

    /// <summary>The text of a heading without any Markdown, since it becomes the section of a rule.</summary>
    public static string Heading(IElement heading)
    {
        var copy = (IElement)heading.Clone(true);
        foreach (IElement sup in copy.QuerySelectorAll("sup").Where(IsFootnoteReference).ToList())
        {
            sup.Remove();
        }

        return Normalizer.Collapse(copy.TextContent);
    }

    /// <summary>A figure caption without the bold that the book sets every caption in; <see cref="Table"/> adds its own.</summary>
    public static string Caption(IElement figcaption)
    {
        return Unbold(Inline(figcaption));
    }

    public static Block? Paragraph(IElement paragraph)
    {
        string text = Inline(paragraph);
        return text.Length == 0 ? null : new Block(BlockKind.Paragraph, text);
    }

    public static Block Code(IElement pre)
    {
        return new Block(BlockKind.Code, Normalizer.Code(pre.TextContent));
    }

    /// <summary>
    /// A sidebar: the author comes from the title line, the text from the remaining paragraphs, code, lists and
    /// tables, separated by blank lines.
    /// </summary>
    public static Block Annotation(IElement sidebar)
    {
        string? author = null;
        var parts = new List<string>();
        foreach (IElement child in sidebar.Children)
        {
            if (child.ClassList.Contains("sb-title"))
            {
                string name = Normalizer.Collapse(child.QuerySelector("strong")?.TextContent ?? string.Empty);
                author = name.Length == 0 ? null : name;
            }
            else if (child.LocalName == "pre")
            {
                parts.Add($"```\n{Normalizer.Code(child.TextContent)}\n```");
            }
            else if (child.LocalName is "ul" or "ol")
            {
                parts.Add(List(child).Text);
            }
            else if (child.LocalName == "table")
            {
                parts.Add(Table(child, caption: null).Text);
            }
            else if (!IsCodeLink(child))
            {
                string text = Inline(child);
                if (text.Length > 0)
                {
                    parts.Add(text);
                }
            }
        }

        return new Block(BlockKind.Annotation, string.Join("\n\n", parts), author);
    }

    public static Block List(IElement list)
    {
        var lines = new List<string>();
        AppendList(list, 0, lines);
        return new Block(BlockKind.List, string.Join('\n', lines));
    }

    /// <summary>A Markdown table; the caption, if any, becomes a bold first line.</summary>
    public static Block Table(IElement table, string? caption)
    {
        var rows = table.QuerySelectorAll("tr")
            .Select(row => row.Children
                .Where(cell => cell.LocalName is "td" or "th")
                .Select(cell => Cell(cell).Replace("|", "\\|"))
                .ToList())
            .Where(cells => cells.Count > 0)
            .ToList();

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(caption))
        {
            lines.Add($"**{caption}**");
            lines.Add(string.Empty);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            lines.Add($"| {string.Join(" | ", rows[i])} |");
            if (i == 0)
            {
                lines.Add($"| {string.Join(" | ", rows[0].Select(_ => "---"))} |");
            }
        }

        return new Block(BlockKind.Table, string.Join('\n', lines));
    }

    /// <summary>The "Click here to view code image" paragraph that precedes each code block.</summary>
    public static bool IsCodeLink(IElement element)
    {
        return element.ClassList.Any(c => c.StartsWith("codelink", StringComparison.Ordinal));
    }

    private static void AppendInline(INode node, StringBuilder builder)
    {
        switch (node)
        {
            case IText text:
                builder.Append(text.Data);
                break;
            case IElement { LocalName: "br" }:
                builder.Append(LineBreak);
                break;
            case IElement { LocalName: "img" }:
                break;
            case IElement { LocalName: "sup" } sup when IsFootnoteReference(sup):
                break;
            case IElement { LocalName: "code" } code:
                string inner = Normalizer.Collapse(code.TextContent);
                if (inner.Length > 0)
                {
                    builder.Append(inner.Contains('`') ? $"`` {inner} ``" : $"`{inner}`");
                }

                break;
            case IElement { LocalName: "em" or "i" } emphasis:
                AppendWrapped(emphasis, "*", builder);
                break;
            case IElement { LocalName: "strong" or "b" } strong:
                AppendWrapped(strong, "**", builder);
                break;
            case IElement { LocalName: "a" } link when link.GetAttribute("href") is string href
                && href.StartsWith("http", StringComparison.Ordinal):
                var label = new StringBuilder();
                AppendChildren(link, label);
                builder.Append($"[{Finish(label.ToString())}]({href})");
                break;
            default:
                AppendChildren(node, builder);
                break;
        }
    }

    /// <summary>A footnote number such as <c>&lt;sup&gt;&lt;a href="#fn1"&gt;1&lt;/a&gt;&lt;/sup&gt;</c>; the footnotes themselves are not rules.</summary>
    private static bool IsFootnoteReference(IElement sup)
    {
        return sup.Children.Length == 1
            && sup.Children[0] is IElement { LocalName: "a" } link
            && link.GetAttribute("href")?.Contains('#') == true
            && !link.GetAttribute("href")!.StartsWith("http", StringComparison.Ordinal)
            && Normalizer.Collapse(sup.TextContent) == Normalizer.Collapse(link.TextContent);
    }

    private static void AppendChildren(INode node, StringBuilder builder)
    {
        foreach (INode child in node.ChildNodes)
        {
            AppendInline(child, builder);
        }
    }

    /// <summary>Wraps the text of an element in markers, leaving the whitespace at either end outside them.</summary>
    private static void AppendWrapped(IElement element, string marker, StringBuilder builder)
    {
        var inner = new StringBuilder();
        AppendChildren(element, inner);
        string text = inner.ToString();
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            builder.Append(text);
            return;
        }

        int start = text.Length - text.TrimStart().Length;
        builder.Append(text[..start])
            .Append(marker).Append(trimmed).Append(marker)
            .Append(text[(start + trimmed.Length)..]);
    }

    /// <summary>Collapses whitespace and turns line breaks into <c>&lt;br&gt;</c>, dropping those at either end.</summary>
    private static string Finish(string text)
    {
        string collapsed = Normalizer.Collapse(text).Trim(LineBreak, ' ');
        return LineBreakWithSpaces().Replace(collapsed, "<br>");
    }

    /// <summary>A header cell drops a bold that covers all of it, since Markdown sets the header row in bold.</summary>
    private static string Cell(IElement cell)
    {
        string text = Inline(cell);
        return cell.LocalName == "th" ? Unbold(text) : text;
    }

    private static string Unbold(string text)
    {
        bool whollyBold = text.Length > 4
            && text.StartsWith("**", StringComparison.Ordinal)
            && text.EndsWith("**", StringComparison.Ordinal)
            && !text[2..^2].Contains("**", StringComparison.Ordinal);
        return whollyBold ? text[2..^2] : text;
    }

    /// <summary>The lines of a Markdown blockquote: the author in bold, then the text of the annotation.</summary>
    private static IEnumerable<string> Quote(Block annotation)
    {
        var lines = new List<string>();
        if (annotation.Author is not null)
        {
            lines.Add($"**{annotation.Author}**");
            lines.Add(string.Empty);
        }

        lines.AddRange(annotation.Text.Split('\n'));
        return lines.Select(l => l.Length == 0 ? ">" : $"> {l}");
    }

    private static void AppendList(IElement list, int depth, List<string> lines)
    {
        string indent = new(' ', depth * 2);
        int number = 1;
        foreach (IElement item in list.Children.Where(c => c.LocalName == "li"))
        {
            string marker = list.LocalName == "ol" ? $"{number++}." : "-";
            var head = new StringBuilder();
            var tail = new List<string>();
            foreach (INode child in item.ChildNodes)
            {
                if (child is IElement { LocalName: "ul" or "ol" } nested)
                {
                    AppendList(nested, depth + 1, tail);
                }
                else if (child is IElement { LocalName: "pre" } pre)
                {
                    tail.AddRange(Normalizer.Code(pre.TextContent).Split('\n').Select(l => $"{indent}  {l}"));
                }
                else if (child is IElement { LocalName: "div" } sidebar && sidebar.ClassList.Contains("sidebar"))
                {
                    string quoteIndent = indent + new string(' ', marker.Length + 1);
                    tail.AddRange(Quote(Annotation(sidebar)).Select(l => quoteIndent + l));
                }
                else if (child is IElement element && IsCodeLink(element))
                {
                    continue;
                }
                else
                {
                    AppendInline(child, head);
                    head.Append(' ');
                }
            }

            lines.Add($"{indent}{marker} {Finish(head.ToString())}");
            lines.AddRange(tail);
        }
    }
}
