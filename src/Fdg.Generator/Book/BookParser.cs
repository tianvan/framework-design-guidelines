using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Book;

/// <summary>Extracts the icon-marked guidelines of one chapter, in book order.</summary>
public static class BookParser
{
    private static readonly string[] CheckIcons = { "check.jpg", "check1.jpg" };
    private static readonly string[] CrossIcons = { "cross.jpg", "cross1.jpg" };

    public static BookChapter Parse(string xhtmlPath, string chapterToken)
    {
        // The files contain named entities and &#13; line ends, so the lenient HTML parser is used.
        using FileStream stream = File.OpenRead(xhtmlPath);
        IHtmlDocument document = new HtmlParser().ParseDocument(stream);
        IElement root = document.Body ?? throw new BookFormatException($"{xhtmlPath}: no <body> element.");

        int iconCount = document.QuerySelectorAll("img").Count(img => IconName(img) is not null);
        var walker = new Walker(chapterToken, isStyle: chapterToken == "A");
        walker.Visit(root);
        IReadOnlyList<Rule> rules = walker.Finish();

        if (rules.Count != iconCount)
        {
            throw new BookFormatException(
                $"{xhtmlPath}: found {iconCount} guideline icons but extracted {rules.Count} guidelines.");
        }

        return new BookChapter(rules, iconCount);
    }

    private static string? IconName(IElement img)
    {
        string? source = img.GetAttribute("src");
        if (source is null)
        {
            return null;
        }

        string name = source[(source.LastIndexOf('/') + 1)..];
        return CheckIcons.Contains(name) || CrossIcons.Contains(name) ? name : null;
    }

    private sealed class Walker
    {
        private readonly string _chapterToken;
        private readonly bool _isStyle;
        private readonly List<Rule> _rules = new();
        private string? _section;
        private string? _anchor;
        private PendingRule? _current;

        // The index of the guideline that introduced the last guideline table; blocks after the table belong to it.
        private int? _tableOwner;

        public Walker(string chapterToken, bool isStyle)
        {
            _chapterToken = chapterToken;
            _isStyle = isStyle;
        }

        public IReadOnlyList<Rule> Finish()
        {
            EndRule();
            return _rules;
        }

        public void Visit(IElement element)
        {
            switch (element.LocalName)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    VisitHeading(element);
                    break;
                case "p":
                    VisitParagraph(element);
                    break;
                case "pre":
                    AddBlock(BlockRenderer.Code(element));
                    break;
                case "ul" or "ol":
                    AddBlock(BlockRenderer.List(element));
                    break;
                case "table":
                    VisitTable(element, caption: null);
                    break;
                case "figure":
                    VisitFigure(element);
                    break;
                case "div" when element.ClassList.Contains("sidebar"):
                    AddBlock(BlockRenderer.Annotation(element));
                    break;
                default:
                    VisitChildren(element);
                    break;
            }
        }

        private void VisitChildren(IElement element)
        {
            foreach (IElement child in element.Children)
            {
                Visit(child);
            }
        }

        private void VisitHeading(IElement heading)
        {
            EndRule();
            _tableOwner = null;
            string? id = heading.GetAttribute("id");
            string text = BlockRenderer.Heading(heading);
            if (!string.IsNullOrEmpty(id) && text.Length > 0)
            {
                _section = text;
                _anchor = id;
            }
        }

        private void VisitParagraph(IElement paragraph)
        {
            if (BlockRenderer.IsCodeLink(paragraph))
            {
                return;
            }

            bool isGuideline = paragraph.ClassList.Contains("check") || paragraph.ClassList.Contains("tab-para1");
            if (isGuideline)
            {
                IElement? icon = paragraph.QuerySelector("img");
                string? iconName = icon is null ? null : IconName(icon);
                if (iconName is null)
                {
                    throw new BookFormatException(
                        $"Guideline paragraph without a check or cross icon: '{BlockRenderer.Inline(paragraph)}'.");
                }

                StartRule(paragraph, iconName);
                return;
            }

            Block? block = BlockRenderer.Paragraph(paragraph);
            if (block is not null)
            {
                AddBlock(block);
            }
        }

        private void VisitFigure(IElement figure)
        {
            IElement? figcaption = figure.QuerySelector("figcaption");
            string? caption = figcaption is null ? null : BlockRenderer.Caption(figcaption);
            foreach (IElement child in figure.Children)
            {
                if (child.LocalName == "figcaption")
                {
                    continue;
                }

                if (child.LocalName == "table")
                {
                    VisitTable(child, caption);
                }
                else
                {
                    Visit(child);
                }
            }
        }

        private void VisitTable(IElement table, string? caption)
        {
            if (table.QuerySelectorAll("img").Any(img => IconName(img) is not null))
            {
                // A table of guidelines: each item is its own rule and has no body. The guideline before the
                // table, if any, introduces it and keeps the blocks that follow the table.
                // Cells without an icon are row context (see RowContext), not body.
                int? owner = _current is null ? null : _rules.Count;
                EndRule();
                foreach (IElement cell in table.QuerySelectorAll("td, th"))
                {
                    if (cell.QuerySelectorAll("img").Any(img => IconName(img) is not null))
                    {
                        VisitChildren(cell);
                    }
                }

                EndRule();
                _tableOwner = owner;
            }
            else
            {
                AddBlock(BlockRenderer.Table(table, caption));
            }
        }

        private void StartRule(IElement paragraph, string iconName)
        {
            EndRule();
            _tableOwner = null;
            string id = $"FDG{_chapterToken}{_rules.Count + 1:000}";

            if (_section is null || _anchor is null)
            {
                throw new BookFormatException($"{id}: guideline appears before any heading.");
            }

            IElement? strong = paragraph.QuerySelector("strong");
            string strengthText = strong is null ? string.Empty : Normalizer.Collapse(strong.TextContent);
            if (!StrengthExtensions.TryParse(strengthText, out Strength strength))
            {
                throw new BookFormatException($"{id}: unknown strength '{strengthText}'.");
            }

            bool iconIsCheck = Array.IndexOf(CheckIcons, iconName) >= 0;
            if (iconIsCheck != strength.UsesCheckIcon())
            {
                string expected = strength.UsesCheckIcon() ? "check" : "cross";
                throw new BookFormatException(
                    $"{id}: strength '{strengthText}' requires a {expected} icon but the book shows '{iconName}'.");
            }

            var copy = (IElement)paragraph.Clone(true);
            copy.QuerySelector("span.middle")?.Remove();
            copy.QuerySelector("strong")?.Remove();

            _current = new PendingRule(
                id,
                strength,
                BlockRenderer.Inline(copy),
                RowContext(paragraph),
                _section,
                _anchor);
        }

        /// <summary>Table items take the first column of their row as context, unless they sit in it.</summary>
        private static string? RowContext(IElement paragraph)
        {
            IElement? row = paragraph.Closest("tr");
            IElement? firstCell = row?.Children.FirstOrDefault(c => c.LocalName is "td" or "th");
            if (firstCell is null || firstCell.Contains(paragraph))
            {
                return null;
            }

            string text = Normalizer.Collapse(firstCell.TextContent);
            return text.Length == 0 ? null : text;
        }

        private void AddBlock(Block block)
        {
            if (_current is not null)
            {
                _current.Body.Add(block);
            }
            else if (_tableOwner is int owner)
            {
                Rule rule = _rules[owner];
                _rules[owner] = rule with { Body = rule.Body.Append(block).ToList() };
            }
        }

        private void EndRule()
        {
            if (_current is null)
            {
                return;
            }

            _rules.Add(new Rule(
                _current.Id,
                _current.Strength,
                _current.Text,
                _current.Context,
                _current.Section,
                _current.Anchor,
                _current.Body,
                RuleMapping.CreateDefault(_current.Strength, _isStyle)));
            _current = null;
        }
    }

    private sealed record PendingRule(
        string Id,
        Strength Strength,
        string Text,
        string? Context,
        string Section,
        string Anchor)
    {
        public List<Block> Body { get; } = new();
    }
}
