using System.Text;
using AngleSharp.Dom;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Book;

/// <summary>Turns book markup into text and <see cref="Block"/> values.</summary>
public static class BlockRenderer
{
    /// <summary>The text of a node with inline code in backticks and whitespace collapsed.</summary>
    public static string Inline(INode node)
    {
        var builder = new StringBuilder();
        AppendInline(node, builder);
        return Normalizer.Collapse(builder.ToString());
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

    /// <summary>A sidebar: the author comes from the title line, the text from the remaining paragraphs.</summary>
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
                .Select(cell => Inline(cell).Replace("|", "\\|"))
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
                builder.Append(' ');
                break;
            case IElement { LocalName: "img" }:
                break;
            case IElement { LocalName: "code" } code:
                string inner = Normalizer.Collapse(code.TextContent);
                if (inner.Length > 0)
                {
                    builder.Append(inner.Contains('`') ? $"`` {inner} ``" : $"`{inner}`");
                }

                break;
            default:
                foreach (INode child in node.ChildNodes)
                {
                    AppendInline(child, builder);
                }

                break;
        }
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
                else
                {
                    AppendInline(child, head);
                    head.Append(' ');
                }
            }

            lines.Add($"{indent}{marker} {Normalizer.Collapse(head.ToString())}");
            lines.AddRange(tail);
        }
    }
}
