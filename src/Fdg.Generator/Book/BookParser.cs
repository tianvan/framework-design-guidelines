using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Fdg.Generator.Rules;

namespace Fdg.Generator.Book;

/// <summary>
/// Extracts the icon-marked guidelines of one chapter, in book order. Appendix D has no guideline paragraphs: each
/// of its h4 headings names a kind of change and carries a verdict icon, and becomes one rule.
/// </summary>
public static class BookParser
{
    private const string AppendixDToken = "D";
    private const string ContextualIcon = "question1.jpg";

    private static readonly string[] CheckIcons = { "check.jpg", "check1.jpg" };
    private static readonly string[] CrossIcons = { "cross.jpg", "cross1.jpg" };

    // Appendix D's legend: the kinds of break a change causes, in the legend's order.
    private static readonly (string Icon, string Kind)[] BreakIcons =
    {
        ("man.jpg", "runtime"),
        ("circ.jpg", "compilation"),
        ("circ1.jpg", "recompile"),
        ("glass.jpg", "reflection"),
    };

    // An appendix D heading ends with its icons in parentheses, and starts with its section number.
    private static readonly Regex IconParentheses = new(@"\s*\(\s*\)\z", RegexOptions.CultureInvariant);
    private static readonly Regex SectionNumber = new(@"\AD(\.[0-9]+)+\s+", RegexOptions.CultureInvariant);

    // Guidelines where the book prints the wrong icon; the strength word wins. Each is matched by file name,
    // strength and the start of its rendered text, so any other icon/strength mismatch still fails.
    private static readonly IconMismatch[] KnownIconMismatches =
    {
        new("ch04.xhtml", Strength.Avoid, "creating overloads across `System.String` and a strongly typed string"),
        new("ch07.xhtml", Strength.Do, "report execution failures by throwing exceptions."),
        new("ch07.xhtml", Strength.Consider, "terminating the process by calling `System.Environment.FailFast`"),
        new("ch09.xhtml", Strength.DoNot, "return tasks in the `Created` state."),
    };

    /// <param name="testProfileOn">False for a chapter that is off in the test profile: its rules start with test severity none.</param>
    public static BookChapter Parse(string xhtmlPath, string chapterToken, bool testProfileOn = true)
    {
        // The files contain named entities and &#13; line ends, so the lenient HTML parser is used.
        using FileStream stream = File.OpenRead(xhtmlPath);
        IHtmlDocument document = new HtmlParser().ParseDocument(stream);
        IElement root = document.Body ?? throw new BookFormatException($"{xhtmlPath}: no <body> element.");

        // Appendix D's legend repeats the verdict icons, so there only headings are counted.
        int iconCount = chapterToken == AppendixDToken
            ? document.QuerySelectorAll("h4").Count(heading => VerdictIcon(heading) is not null)
            : document.QuerySelectorAll("img").Count(img => IconName(img) is not null);
        var walker = new Walker(Path.GetFileName(xhtmlPath), chapterToken, isStyle: chapterToken == "A", testProfileOn);
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
        string? name = ImageName(img);
        return CheckIcons.Contains(name) || CrossIcons.Contains(name) ? name : null;
    }

    private static string? ImageName(IElement img)
    {
        string? source = img.GetAttribute("src");
        return source?[(source.LastIndexOf('/') + 1)..];
    }

    /// <summary>The verdict an appendix D heading carries, or null if it has none.</summary>
    private static Strength? VerdictIcon(IElement heading)
    {
        foreach (string? name in heading.QuerySelectorAll("img").Select(ImageName))
        {
            if (CheckIcons.Contains(name))
            {
                return Strength.Permitted;
            }

            if (CrossIcons.Contains(name))
            {
                return Strength.NotPermitted;
            }

            if (name == ContextualIcon)
            {
                return Strength.Contextual;
            }
        }

        return null;
    }

    /// <summary>The kinds of break an appendix D heading's icons show, e.g. "runtime and compilation breaks".</summary>
    private static string? BreakKinds(IElement heading)
    {
        var names = heading.QuerySelectorAll("img").Select(ImageName).ToHashSet();
        string[] kinds = BreakIcons.Where(icon => names.Contains(icon.Icon)).Select(icon => icon.Kind).ToArray();
        return kinds.Length switch
        {
            0 => null,
            1 => $"{kinds[0]} break",
            _ => $"{string.Join(", ", kinds[..^1])} and {kinds[^1]} breaks",
        };
    }

    /// <summary>A copy of an appendix D heading without its icons; the parentheses that held them remain.</summary>
    private static IElement WithoutIcons(IElement heading)
    {
        var copy = (IElement)heading.Clone(true);
        foreach (IElement icon in copy.QuerySelectorAll("span.middle").ToList())
        {
            icon.Remove();
        }

        return copy;
    }

    private sealed class Walker
    {
        private readonly string _fileName;
        private readonly string _chapterToken;
        private readonly bool _isStyle;
        private readonly bool _testProfileOn;
        private readonly List<Rule> _rules = new();
        private string? _section;
        private string? _anchor;
        private PendingRule? _current;

        // The index of the guideline that introduced the last guideline table; blocks after the table belong to it.
        private int? _tableOwner;

        public Walker(string fileName, string chapterToken, bool isStyle, bool testProfileOn)
        {
            _fileName = fileName;
            _chapterToken = chapterToken;
            _isStyle = isStyle;
            _testProfileOn = testProfileOn;
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
            bool isAppendixD = _chapterToken == AppendixDToken;
            IElement source = isAppendixD ? WithoutIcons(heading) : heading;
            string? id = heading.GetAttribute("id");
            string text = BlockRenderer.Heading(source);
            if (isAppendixD)
            {
                text = IconParentheses.Replace(text, string.Empty);
            }

            if (!string.IsNullOrEmpty(id) && text.Length > 0)
            {
                _section = text;
                _anchor = id;
            }

            if (isAppendixD && heading.LocalName == "h4")
            {
                StartChangeRule(heading, source, text, id);
            }
        }

        /// <summary>An appendix D heading: its verdict is the strength, its break kinds the context.</summary>
        private void StartChangeRule(IElement heading, IElement withoutIcons, string section, string? anchor)
        {
            string id = $"FDG{_chapterToken}{_rules.Count + 1:000}";
            Strength strength = VerdictIcon(heading)
                ?? throw new BookFormatException($"{id}: appendix D heading '{section}' has no verdict icon.");
            if (string.IsNullOrEmpty(anchor) || section.Length == 0)
            {
                throw new BookFormatException($"{id}: appendix D heading '{section}' has no id or no text.");
            }

            string title = IconParentheses.Replace(BlockRenderer.Inline(withoutIcons), string.Empty);
            string text = SectionNumber.Replace(title, string.Empty);
            _current = new PendingRule(id, strength, text, BreakKinds(heading), section, anchor);
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

            var copy = (IElement)paragraph.Clone(true);
            copy.QuerySelector("span.middle")?.Remove();
            copy.QuerySelector("strong")?.Remove();
            string text = BlockRenderer.Inline(copy);

            bool iconIsCheck = Array.IndexOf(CheckIcons, iconName) >= 0;
            if (iconIsCheck != strength.UsesCheckIcon() && !IsKnownIconMismatch(strength, text))
            {
                string expected = strength.UsesCheckIcon() ? "check" : "cross";
                throw new BookFormatException(
                    $"{id}: strength '{strengthText}' requires a {expected} icon but the book shows '{iconName}'.");
            }

            _current = new PendingRule(
                id,
                strength,
                text,
                RowContext(paragraph),
                _section,
                _anchor);
        }

        private bool IsKnownIconMismatch(Strength strength, string text)
        {
            return KnownIconMismatches.Any(known =>
                known.FileName == _fileName
                && known.Strength == strength
                && text.StartsWith(known.TextPrefix, StringComparison.Ordinal));
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
                RuleMapping.CreateDefault(_current.Strength, _isStyle, _testProfileOn)));
            _current = null;
        }
    }

    private sealed record IconMismatch(string FileName, Strength Strength, string TextPrefix);

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
