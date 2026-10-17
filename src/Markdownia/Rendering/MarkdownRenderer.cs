using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.SmartyPants;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdownia.Theming;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using AvInline = Avalonia.Controls.Documents.Inline;
using Path = Avalonia.Controls.Shapes.Path;

namespace Markdownia.Rendering;

/// <summary>
/// A request to change an image's width in the source. <see cref="Start"/>/<see cref="End"/> delimit the image
/// syntax (<c>![alt](src)</c> or <c>&lt;img …&gt;</c>) in the parsed text, and <see cref="Original"/> is that text,
/// used to check the document hasn't changed since. A null <see cref="Width"/> removes the size.
/// </summary>
public sealed record ImageResize(int Start, int End, string Original, bool IsHtml, int? Width);

/// <summary>The rendered preview plus the bookkeeping needed for scroll sync and in-document links.</summary>
public sealed class RenderResult(Control root, List<(int Line, Control Control)> lineAnchors, Dictionary<string, Control> anchors)
{
    public Control Root { get; } = root;
    public IReadOnlyList<(int Line, Control Control)> LineAnchors { get; } = lineAnchors;
    public IReadOnlyDictionary<string, Control> Anchors { get; } = anchors;
}

/// <summary>
/// Converts a Markdig syntax tree into native Avalonia controls. Every paragraph becomes a selectable
/// <see cref="RichTextBlock"/>; tables, lists, code, math and callouts become composed layout controls.
/// </summary>
public sealed partial class MarkdownRenderer
{
    private static readonly FontFeatureCollection CodeFeatures = [FontFeature.Parse("-calt"), FontFeature.Parse("-liga")];

    private readonly PreviewStyle _s;
    private readonly MarkdownTheme _t;
    private readonly Action<string> _onLink;
    private readonly Action<ImageResize>? _onImageResize;
    private readonly List<(int Line, Control Control)> _lineAnchors = [];
    private readonly Dictionary<string, Control> _anchors = new(StringComparer.Ordinal);
    private string _source = "";

    // Ascent + descent, and the default line height (ascent + descent + line gap), of the body font, in ems.
    private readonly double _glyphExtent, _naturalLine;

    private readonly IBrush _fg, _heading, _muted, _accent, _rule, _codeBg, _codeBorder, _quoteBar, _highlight,
        _tableHeader, _tableStripe, _selection, _linkUnderline;

    public MarkdownRenderer(PreviewStyle style, Action<string> onLink, Action<ImageResize>? onImageResize = null)
    {
        _s = style;
        _t = style.Theme;
        _onLink = onLink;
        _onImageResize = onImageResize;
        _fg = new SolidColorBrush(_t.Foreground);
        _heading = new SolidColorBrush(_t.Heading);
        _muted = new SolidColorBrush(_t.Muted);
        _accent = new SolidColorBrush(_t.Accent);
        _rule = new SolidColorBrush(_t.Rule);
        _codeBg = new SolidColorBrush(_t.CodeBackground);
        _codeBorder = new SolidColorBrush(_t.CodeBorder);
        _quoteBar = new SolidColorBrush(_t.QuoteBar);
        _highlight = new SolidColorBrush(_t.Highlight);
        _tableHeader = new SolidColorBrush(_t.TableHeader);
        _tableStripe = new SolidColorBrush(_t.TableStripe);
        _selection = new SolidColorBrush(_t.Selection);
        _linkUnderline = new SolidColorBrush(_t.Accent, 0.45);
        (_glyphExtent, _naturalLine) = FontExtents(style.BodyFont);
    }

    private static (double Glyphs, double Line) FontExtents(FontFamily family)
    {
        try
        {
            var metrics = new Typeface(family).GlyphTypeface.Metrics;
            var glyphs = (Math.Abs(metrics.Ascent) + Math.Abs(metrics.Descent)) / (double)metrics.DesignEmHeight;
            return (glyphs, glyphs + Math.Max(0, metrics.LineGap) / (double)metrics.DesignEmHeight);
        }
        catch (Exception)
        {
            return (1.2, 1.2);
        }
    }

    private double Em => _s.FontSize;

    private readonly record struct BlockContext(IBrush Foreground, double FontSize, bool Tight);

    public RenderResult Render(ParsedDocument parsed)
    {
        _source = parsed.Source;
        var root = new StackPanel();
        if (!parsed.Metadata.IsEmpty)
            root.Children.Add(TitleBlock(parsed.Metadata));
        RenderBlocks(parsed.Document, root.Children, new BlockContext(_fg, Em, false));
        CollapseOuterMargins(root.Children);
        _lineAnchors.Sort((a, b) => a.Line.CompareTo(b.Line));
        return new RenderResult(root, _lineAnchors, _anchors);
    }

    // ------------------------------------------------------------------ blocks

    private void RenderBlocks(ContainerBlock container, Avalonia.Controls.Controls target, BlockContext ctx)
    {
        foreach (var block in container)
        {
            var control = RenderBlock(block, ctx);
            if (control is null)
                continue;
            target.Add(control);
            _lineAnchors.Add((block.Line, control));
        }
    }

    private static Thickness WithTop(Thickness t, double top) => new(t.Left, top, t.Right, t.Bottom);

    private static Thickness WithBottom(Thickness t, double bottom) => new(t.Left, t.Top, t.Right, bottom);

    private static void CollapseOuterMargins(Avalonia.Controls.Controls children)
    {
        if (children.Count == 0)
            return;
        var first = children[0];
        first.Margin = WithTop(first.Margin, 0);
        var last = children[^1];
        last.Margin = WithBottom(last.Margin, 0);
    }

    private Control? RenderBlock(MdBlock block, BlockContext ctx) => block switch
    {
        YamlFrontMatterBlock => null,
        LinkReferenceDefinitionGroup => null,
        BlankLineBlock => null,
        Abbreviation => null,
        HeadingBlock heading => Heading(heading, ctx),
        MathBlock math => MathBlockControl(math.Lines.ToString(), ctx),
        FencedCodeBlock fenced when SyntaxHighlighter.NormalizeLanguage(fenced.Info) == "math" => MathBlockControl(fenced.Lines.ToString(), ctx),
        CodeBlock code => CodeBlockControl(code),
        ParagraphBlock paragraph => Paragraph(paragraph, ctx),
        AlertBlock alert => Alert(alert, AlertKind(alert.Kind.ToString()), ctx),
        QuoteBlock quote => Quote(quote, ctx),
        ListBlock list => List(list, ctx, 0),
        ThematicBreakBlock => ThematicBreak(),
        Table table => TableControl(table, ctx),
        FootnoteGroup footnotes => Footnotes(footnotes, ctx),
        DefinitionList definitions => DefinitionListControl(definitions, ctx),
        CustomContainer div => FencedDiv(div, ctx),
        HtmlBlock html => Html(html, ctx),
        ContainerBlock other => Stack(other, ctx),
        LeafBlock leaf when leaf.Inline is not null => Paragraph(leaf, ctx),
        _ => null,
    };

    private StackPanel Stack(ContainerBlock container, BlockContext ctx)
    {
        var panel = new StackPanel();
        RenderBlocks(container, panel.Children, ctx);
        CollapseOuterMargins(panel.Children);
        return panel;
    }

    private RichTextBlock NewText(BlockContext ctx, double fontSize, double lineSpacing)
    {
        var lineHeight = Math.Round(fontSize * lineSpacing);
        var text = new RichTextBlock
        {
            FontFamily = _s.BodyFont,
            FontSize = fontSize,
            Foreground = ctx.Foreground,
            LineHeight = lineHeight,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = _s.Justify ? TextAlignment.Justify : TextAlignment.Left,
            SelectionBrush = _selection,
            LinkActivated = _onLink,
            ClipToBounds = false,
        };
        // When the line height is smaller than the font's ascent + descent (headings, titles), Avalonia keeps the
        // baseline at the full ascent and the descenders hang below the control. Center the glyphs in the line box
        // instead, like CSS half-leading.
        var overflow = _glyphExtent * fontSize - lineHeight;
        if (overflow > 1)
            text.RenderTransform = new TranslateTransform(0, -Math.Round(overflow / 2));
        return text;
    }

    private Control Paragraph(LeafBlock paragraph, BlockContext ctx)
    {
        // Pandoc "implicit figure": an image alone in a paragraph becomes a captioned figure.
        if (paragraph.Inline is { } inlines && SoleImage(inlines) is { } image)
            return Figure(image, ctx);

        var text = NewText(ctx, ctx.FontSize, _s.LineSpacing);
        BuildInlines(text, paragraph.Inline, ctx);
        text.Margin = new Thickness(0, 0, 0, ctx.Tight ? 0 : Math.Round(ctx.FontSize * 0.95));
        return text;
    }

    private static LinkInline? SoleImage(ContainerInline inlines)
    {
        LinkInline? image = null;
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LinkInline { IsImage: true } link when image is null:
                    image = link;
                    break;
                case LiteralInline literal when literal.Content.IsEmptyOrWhitespace():
                case LineBreakInline:
                    break;
                default:
                    return null;
            }
        }
        return image;
    }

    private Control Heading(HeadingBlock heading, BlockContext ctx)
    {
        var (scale, spacing) = heading.Level switch
        {
            1 => (2.0, 1.2),
            2 => (1.55, 1.25),
            3 => (1.28, 1.3),
            4 => (1.1, 1.35),
            5 => (1.0, 1.4),
            _ => (0.92, 1.4),
        };
        var size = Math.Round(Em * scale);
        var text = NewText(ctx with { Foreground = heading.Level >= 6 ? _muted : _heading }, size, spacing);
        text.FontWeight = FontWeight.SemiBold;
        if (heading.Level <= 2)
            text.LetterSpacing = -0.01 * size;
        text.TextAlignment = TextAlignment.Left;
        BuildInlines(text, heading.Inline, ctx with { FontSize = size });

        var top = heading.Level switch { 1 => 1.9, 2 => 1.7, 3 => 1.45, _ => 1.25 } * Em;
        var bottom = heading.Level <= 2 ? 0.7 * Em : 0.5 * Em;
        text.Margin = new Thickness(0, Math.Round(top), 0, Math.Round(bottom));

        if (heading.GetAttributes().Id is { Length: > 0 } id)
            _anchors.TryAdd(id, text);
        return text;
    }

    private Control TitleBlock(DocumentMetadata meta)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, Math.Round(Em * 2.2)) };
        var ctx = new BlockContext(_heading, Em, false);
        if (meta.Title is not null)
        {
            var size = Math.Round(Em * 2.35);
            var title = NewText(ctx, size, 1.15);
            title.FontWeight = FontWeight.SemiBold;
            title.LetterSpacing = -0.015 * size;
            title.TextAlignment = TextAlignment.Left;
            title.Inlines!.Add(new Run(meta.Title));
            panel.Children.Add(title);
        }
        if (meta.Subtitle is not null)
        {
            var size = Math.Round(Em * 1.3);
            var subtitle = NewText(ctx with { Foreground = _muted }, size, 1.3);
            subtitle.FontStyle = FontStyle.Italic;
            subtitle.Margin = new Thickness(0, Em * 0.35, 0, 0);
            subtitle.Inlines!.Add(new Run(meta.Subtitle));
            panel.Children.Add(subtitle);
        }
        var byline = string.Join(" · ", meta.Authors.Append(meta.Date).Where(s => !string.IsNullOrWhiteSpace(s)));
        if (byline.Length > 0)
        {
            var text = NewText(ctx with { Foreground = _muted }, Math.Round(Em * 0.92), 1.4);
            text.Margin = new Thickness(0, Em * 0.8, 0, 0);
            text.LetterSpacing = 0.02 * Em;
            text.Inlines!.Add(new Run(byline));
            panel.Children.Add(text);
        }
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = _rule,
            Margin = new Thickness(0, Em * 1.2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Em * 4,
        });
        return panel;
    }

    private Control Quote(QuoteBlock quote, BlockContext ctx)
    {
        var quoteColor = ThemeManager.Blend(_t.Foreground, _t.Background, 0.78);
        var inner = Stack(quote, ctx with { Foreground = new SolidColorBrush(quoteColor), Tight = false });
        return new Border
        {
            BorderBrush = _quoteBar,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(Math.Round(Em * 1.0), 2, 0, 2),
            Margin = new Thickness(0, 0, 0, Math.Round(Em * 1.1)),
            Child = inner,
        };
    }

    private enum Callout { Note, Tip, Important, Warning, Caution }

    private static Callout? AlertKind(string kind) => kind.Trim().ToUpperInvariant() switch
    {
        "NOTE" or "INFO" => Callout.Note,
        "TIP" or "HINT" => Callout.Tip,
        "IMPORTANT" => Callout.Important,
        "WARNING" or "ATTENTION" => Callout.Warning,
        "CAUTION" or "DANGER" or "ERROR" => Callout.Caution,
        _ => null,
    };

    private Control Alert(ContainerBlock block, Callout? kind, BlockContext ctx, string? customTitle = null)
    {
        var k = kind ?? Callout.Note;
        var dark = _t.IsDark;
        var color = Color.Parse(k switch
        {
            Callout.Note => dark ? "#4493F8" : "#0969DA",
            Callout.Tip => dark ? "#3FB950" : "#1A7F37",
            Callout.Important => dark ? "#AB7DF8" : "#8250DF",
            Callout.Warning => dark ? "#D29922" : "#9A6700",
            _ => dark ? "#F85149" : "#CF222E",
        });
        var iconKey = k switch
        {
            Callout.Note => "Icon.InformationOutline",
            Callout.Tip => "Icon.Check",
            Callout.Important => "Icon.AlertCircleOutline",
            Callout.Warning => "Icon.AlertCircleOutline",
            _ => "Icon.AlertCircleOutline",
        };
        var brush = new SolidColorBrush(color);
        var titleText = customTitle ?? k.ToString();

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Em * 0.45, Margin = new Thickness(0, 0, 0, Em * 0.4) };
        if (Application.Current?.FindResource(iconKey) is Geometry geometry)
        {
            header.Children.Add(new Path
            {
                Data = geometry,
                Fill = brush,
                Stretch = Stretch.Uniform,
                Width = Math.Round(Em * 0.95),
                Height = Math.Round(Em * 0.95),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        header.Children.Add(new TextBlock
        {
            Text = titleText,
            FontFamily = _s.BodyFont,
            FontSize = Math.Round(Em * 0.95),
            FontWeight = FontWeight.SemiBold,
            Foreground = brush,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var content = new StackPanel();
        content.Children.Add(header);
        var body = Stack(block, ctx with { Tight = false });
        content.Children.Add(body);

        return new Border
        {
            BorderBrush = brush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = new SolidColorBrush(color, dark ? 0.08 : 0.06),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Padding = new Thickness(Math.Round(Em), Math.Round(Em * 0.7), Math.Round(Em), Math.Round(Em * 0.75)),
            Margin = new Thickness(0, Em * 0.2, 0, Math.Round(Em * 1.2)),
            Child = content,
        };
    }

    private Control FencedDiv(CustomContainer div, BlockContext ctx)
    {
        var info = div.Info?.Trim() ?? "";
        var cls = info.TrimStart('{', '.').Split(' ', '}')[0];
        if (AlertKind(cls) is { } kind)
        {
            var title = div.Arguments is { Length: > 0 } args ? args.Trim() : null;
            return Alert(div, kind, ctx, title);
        }
        var inner = Stack(div, ctx);
        return new Border
        {
            Background = new SolidColorBrush(_t.CodeBackground, 0.6),
            BorderBrush = _codeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(Math.Round(Em), Math.Round(Em * 0.8)),
            Margin = new Thickness(0, 0, 0, Math.Round(Em * 1.1)),
            Child = inner,
        };
    }

    private Control ThematicBreak() => new Border
    {
        Height = 1,
        Background = _rule,
        Margin = new Thickness(0, Math.Round(Em * 1.4), 0, Math.Round(Em * 1.6)),
    };

    private Control List(ListBlock list, BlockContext ctx, int depth)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, ctx.Tight ? Math.Round(Em * 0.25) : Math.Round(Em * 0.95)) };
        var itemCtx = ctx with { Tight = !list.IsLoose };
        var ordered = list.IsOrdered;
        var number = 1;
        if (ordered && list.OrderedStart is { } start)
            number = ParseOrderedStart(start, list.BulletType);
        var markerWidth = ordered
            ? Math.Ceiling(ctx.FontSize * (0.62 * Math.Max(2, MarkerText(list, number + list.Count - 1).Length) + 0.5))
            : Math.Round(ctx.FontSize * 1.35);

        foreach (var block in list)
        {
            if (block is not ListItemBlock item)
                continue;
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"{markerWidth},*"),
                Margin = new Thickness(0, 0, 0, list.IsLoose ? Math.Round(ctx.FontSize * 0.6) : Math.Round(ctx.FontSize * 0.22)),
            };

            Control marker;
            var task = FirstTaskList(item);
            if (task is not null)
                marker = TaskBox(task.Checked, ctx.FontSize);
            else
            {
                var text = ordered ? MarkerText(list, number) : depth switch { 0 => "•", 1 => "◦", _ => "▪" };
                var tb = new TextBlock
                {
                    Text = text,
                    FontFamily = _s.BodyFont,
                    FontSize = ctx.FontSize,
                    LineHeight = Math.Round(ctx.FontSize * _s.LineSpacing),
                    Foreground = ordered ? ctx.Foreground : _muted,
                    HorizontalAlignment = ordered ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    Margin = new Thickness(ordered ? 0 : ctx.FontSize * 0.3, 0, ordered ? ctx.FontSize * 0.45 : 0, 0),
                    FontFeatures = [FontFeature.Parse("tnum")],
                };
                marker = tb;
            }
            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            var content = new StackPanel();
            foreach (var child in item)
            {
                var control = child is ListBlock nested ? List(nested, itemCtx, depth + 1) : RenderBlock(child, itemCtx);
                if (control is null)
                    continue;
                content.Children.Add(control);
                _lineAnchors.Add((child.Line, control));
            }
            CollapseOuterMargins(content.Children);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
            number++;
        }
        if (panel.Children.Count > 0)
            panel.Children[^1].Margin = default;
        return panel;
    }

    private static TaskList? FirstTaskList(ListItemBlock item) =>
        item.Count > 0 && item[0] is ParagraphBlock { Inline.FirstChild: TaskList task } ? task : null;

    private Control TaskBox(bool isChecked, double fontSize)
    {
        var size = Math.Round(fontSize * 0.82);
        var box = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Max(2, size * 0.2)),
            BorderThickness = new Thickness(isChecked ? 0 : 1.5),
            BorderBrush = _muted,
            Background = isChecked ? _accent : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            // Center the box on the first line of text.
            Margin = new Thickness(fontSize * 0.1, Math.Round((fontSize * _s.LineSpacing - size) / 2), 0, 0),
        };
        if (isChecked && Application.Current?.FindResource("Icon.Check") is Geometry check)
        {
            box.Child = new Path
            {
                Data = check,
                Fill = new SolidColorBrush(ThemeManager.Luminance(_t.Accent) > 0.55 ? Color.Parse("#1A1A1A") : Colors.White),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(size * 0.12),
            };
        }
        return box;
    }

    private static int ParseOrderedStart(string start, char bulletType)
    {
        if (int.TryParse(start, out var n))
            return n;
        if (start.Length == 1 && char.IsLetter(start[0]) && bulletType is 'a' or 'A')
            return char.ToLowerInvariant(start[0]) - 'a' + 1;
        if (bulletType is 'i' or 'I')
            return FromRoman(start);
        return 1;
    }

    private static string MarkerText(ListBlock list, int n)
    {
        var delimiter = list.OrderedDelimiter == ')' ? ")" : ".";
        return list.BulletType switch
        {
            'a' => ToAlpha(n).ToLowerInvariant() + delimiter,
            'A' => ToAlpha(n) + delimiter,
            'i' => ToRoman(n).ToLowerInvariant() + delimiter,
            'I' => ToRoman(n) + delimiter,
            _ => n + delimiter,
        };
    }

    private static string ToAlpha(int n)
    {
        var s = "";
        while (n > 0)
        {
            n--;
            s = (char)('A' + n % 26) + s;
            n /= 26;
        }
        return s.Length == 0 ? "A" : s;
    }

    private static readonly (int Value, string Numeral)[] RomanTable =
        [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];

    private static string ToRoman(int n)
    {
        if (n <= 0 || n > 3999)
            return n.ToString();
        var s = new System.Text.StringBuilder();
        foreach (var (value, numeral) in RomanTable)
        {
            while (n >= value)
            {
                s.Append(numeral);
                n -= value;
            }
        }
        return s.ToString();
    }

    private static int FromRoman(string s)
    {
        var total = 0;
        var upper = s.ToUpperInvariant();
        for (var i = 0; i < upper.Length; i++)
        {
            var v = RomanTable.FirstOrDefault(r => r.Numeral.Length == 1 && r.Numeral[0] == upper[i]).Value;
            var next = i + 1 < upper.Length ? RomanTable.FirstOrDefault(r => r.Numeral.Length == 1 && r.Numeral[0] == upper[i + 1]).Value : 0;
            total += v < next ? -v : v;
        }
        return Math.Max(1, total);
    }

    private Control CodeBlockControl(CodeBlock code)
    {
        var lang = code is FencedCodeBlock fenced ? SyntaxHighlighter.NormalizeLanguage(fenced.Info) : null;
        var source = code.Lines.ToString().TrimEnd('\n', '\r');
        var size = Math.Round(Em * 0.82);

        var text = new RichTextBlock
        {
            FontFamily = _s.MonoFont,
            FontSize = size,
            LineHeight = Math.Round(size * 1.55),
            Foreground = _fg,
            TextWrapping = TextWrapping.NoWrap,
            SelectionBrush = _selection,
            Padding = new Thickness(Math.Round(Em * 0.95), Math.Round(Em * 0.8)),
            FontFeatures = CodeFeatures,
        };
        var inlines = text.Inlines!;
        foreach (var token in SyntaxHighlighter.Tokenize(source, lang))
        {
            var run = new Run(source.Substring(token.Start, token.Length));
            if (token.Kind != TokenKind.Plain)
                run.Foreground = new SolidColorBrush(TokenColor(token.Kind));
            if (token.Kind == TokenKind.Comment)
                run.FontStyle = FontStyle.Italic;
            inlines.Add(run);
        }

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = text,
        };

        var label = new TextBlock
        {
            Text = SyntaxHighlighter.DisplayName(lang),
            FontFamily = FontFamily.Default,
            FontSize = Math.Max(11, Math.Round(Em * 0.62)),
            Foreground = _muted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var copy = new Button
        {
            Content = "Copy",
            FontSize = Math.Max(11, Math.Round(Em * 0.62)),
            Padding = new Thickness(8, 2),
            MinHeight = 0,
            Classes = { "code-copy" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(source);
                copy.Content = "Copied";
                await Task.Delay(1200);
                copy.Content = "Copy";
            }
        };
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 8, 0),
            Children = { label, copy },
        };
        copy.Opacity = 0;

        var grid = new Grid { Children = { scroller, tools } };
        var border = new Border
        {
            Background = _codeBg,
            BorderBrush = _codeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, Em * 0.15, 0, Math.Round(Em * 1.1)),
            Child = grid,
            ClipToBounds = true,
        };
        border.PointerEntered += (_, _) => copy.Opacity = 1;
        border.PointerExited += (_, _) => copy.Opacity = 0;
        return border;
    }

    private Color TokenColor(TokenKind kind) => kind switch
    {
        TokenKind.Keyword => _t.Syntax.Keyword,
        TokenKind.Type => _t.Syntax.Type,
        TokenKind.String => _t.Syntax.String,
        TokenKind.Number => _t.Syntax.Number,
        TokenKind.Comment => _t.Syntax.Comment,
        TokenKind.Preprocessor => _t.Syntax.Preprocessor,
        TokenKind.Function => _t.Syntax.Function,
        TokenKind.Property => _t.Syntax.Property,
        _ => _t.Foreground,
    };

    private Control MathBlockControl(string latex, BlockContext ctx)
    {
        var math = new MathElement(latex.Trim(), Math.Round(ctx.FontSize * 1.12), _t.Foreground, ErrorColor, display: true)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = math,
            Padding = new Thickness(0, Em * 0.2),
            Margin = new Thickness(0, Math.Round(Em * 0.3), 0, Math.Round(Em * 1.2)),
        };
    }

    private Color ErrorColor => _t.IsDark ? Color.Parse("#F85149") : Color.Parse("#CF222E");

    private Control TableControl(Table table, BlockContext ctx)
    {
        var grid = new Grid();
        var columnCount = Math.Max(table.ColumnDefinitions.Count, table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max());
        for (var i = 0; i < columnCount; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var size = Math.Round(ctx.FontSize * 0.92);
        var rowIndex = 0;
        var bodyRow = 0;
        foreach (var rowBlock in table)
        {
            if (rowBlock is not TableRow row)
                continue;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var isHeader = row.IsHeader;
            IBrush? background = isHeader ? _tableHeader : bodyRow % 2 == 1 ? _tableStripe : null;

            var col = 0;
            foreach (var cellBlock in row)
            {
                if (cellBlock is not TableCell cell)
                    continue;
                var columnIndex = cell.ColumnIndex >= 0 ? cell.ColumnIndex : col;
                var alignment = columnIndex < table.ColumnDefinitions.Count
                    ? table.ColumnDefinitions[columnIndex].Alignment switch
                    {
                        TableColumnAlign.Center => TextAlignment.Center,
                        TableColumnAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left,
                    }
                    : TextAlignment.Left;

                var content = new StackPanel { MaxWidth = Em * 26 };
                var cellCtx = ctx with { FontSize = size, Tight = true };
                foreach (var child in cell)
                {
                    var control = RenderBlock(child, cellCtx);
                    if (control is null)
                        continue;
                    if (control is TextBlock tb)
                    {
                        tb.TextAlignment = alignment;
                        tb.LineHeight = Math.Round(size * 1.45);
                        if (isHeader)
                            tb.FontWeight = FontWeight.SemiBold;
                    }
                    content.Children.Add(control);
                }
                CollapseOuterMargins(content.Children);

                var border = new Border
                {
                    Background = background,
                    BorderBrush = _rule,
                    BorderThickness = new Thickness(0, 0, 0, isHeader ? 1.5 : 1),
                    Padding = new Thickness(Math.Round(Em * 0.8), Math.Round(Em * 0.45)),
                    Child = content,
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, Math.Min(columnIndex, columnCount - 1));
                if (cell.ColumnSpan > 1)
                    Grid.SetColumnSpan(border, cell.ColumnSpan);
                if (cell.RowSpan > 1)
                    Grid.SetRowSpan(border, cell.RowSpan);
                grid.Children.Add(border);
                col = columnIndex + Math.Max(1, cell.ColumnSpan);
            }

            // Fill short rows so striping and rules span the full width.
            for (; col < columnCount; col++)
            {
                var filler = new Border { Background = background, BorderBrush = _rule, BorderThickness = new Thickness(0, 0, 0, 1) };
                Grid.SetRow(filler, rowIndex);
                Grid.SetColumn(filler, col);
                grid.Children.Add(filler);
            }
            rowIndex++;
            if (!isHeader)
                bodyRow++;
        }

        var frame = new Border
        {
            BorderBrush = _rule,
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = frame,
            Margin = new Thickness(0, Em * 0.2, 0, Math.Round(Em * 1.2)),
        };
    }

    private Control Footnotes(FootnoteGroup group, BlockContext ctx)
    {
        var size = Math.Round(Em * 0.86);
        var noteCtx = new BlockContext(_fg, size, false);
        var panel = new StackPanel { Margin = new Thickness(0, Math.Round(Em * 2.4), 0, 0) };
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = _rule,
            Width = Em * 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, Math.Round(Em * 1.0)),
        });

        foreach (var block in group)
        {
            if (block is not Footnote note)
                continue;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Round(size * 2.0)},*"), Margin = new Thickness(0, 0, 0, Math.Round(size * 0.5)) };
            var number = new TextBlock
            {
                Text = note.Order + ".",
                FontFamily = _s.BodyFont,
                FontSize = size,
                LineHeight = Math.Round(size * _s.LineSpacing),
                Foreground = _muted,
                FontFeatures = [FontFeature.Parse("tnum")],
            };
            row.Children.Add(number);
            var content = Stack(note, noteCtx with { Tight = true });
            foreach (var child in content.Children.OfType<TextBlock>())
                child.Margin = WithBottom(child.Margin, Math.Round(size * 0.4));
            CollapseOuterMargins(content.Children);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
            _anchors.TryAdd("fn:" + note.Order, row);
            _lineAnchors.Add((note.Line, row));
        }
        return panel;
    }

    private Control DefinitionListControl(DefinitionList list, BlockContext ctx)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, Math.Round(Em * 0.95)) };
        foreach (var block in list)
        {
            if (block is not DefinitionItem item)
                continue;
            foreach (var child in item)
            {
                if (child is DefinitionTerm term)
                {
                    var text = NewText(ctx, ctx.FontSize, _s.LineSpacing);
                    text.FontWeight = FontWeight.SemiBold;
                    BuildInlines(text, term.Inline, ctx);
                    text.Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : Math.Round(Em * 0.6), 0, Math.Round(Em * 0.15));
                    panel.Children.Add(text);
                }
                else if (RenderBlock(child, ctx with { Tight = true }) is { } control)
                {
                    var holder = new Border
                    {
                        BorderBrush = _rule,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(Math.Round(Em * 1.6), 0, 0, 0),
                        Margin = new Thickness(0, 0, 0, Math.Round(Em * 0.25)),
                        Child = control,
                    };
                    control.Margin = WithBottom(control.Margin, 0);
                    panel.Children.Add(holder);
                }
            }
        }
        return panel;
    }

    private Control? Html(HtmlBlock html, BlockContext ctx)
    {
        if (html.Type == HtmlBlockType.Comment)
            return null;
        var raw = html.Lines.ToString();

        // Render images referenced by <img src="..."> and the visible text; other markup is dropped.
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, Math.Round(Em * 0.95)) };
        foreach (System.Text.RegularExpressions.Match m in HtmlImageTagRegex().Matches(raw))
        {
            // Locate the tag in the source (block lines may have container prefixes stripped).
            var start = _source.IndexOf(m.Value, Math.Max(0, html.Span.Start), StringComparison.Ordinal);
            var image = HtmlImage(m.Value, start >= 0 && start + m.Length <= html.Span.End + 1 ? start : -1);
            if (image is null)
                continue;
            image.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(image);
        }
        var text = System.Net.WebUtility.HtmlDecode(HtmlTagRegex().Replace(raw, " "));
        text = string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
        if (text.Length > 0)
        {
            var tb = NewText(ctx, ctx.FontSize, _s.LineSpacing);
            tb.Inlines!.Add(new Run(text));
            panel.Children.Add(tb);
        }
        return panel.Children.Count == 0 ? null : panel;
    }

    [System.Text.RegularExpressions.GeneratedRegex("<img\\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex HtmlImageTagRegex();

    [System.Text.RegularExpressions.GeneratedRegex("\\b(src|alt|title|width|height)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex HtmlAttributeRegex();

    /// <summary>An image for an HTML <c>&lt;img&gt;</c> tag starting at <paramref name="sourceStart"/> in the source.</summary>
    private Control? HtmlImage(string tag, int sourceStart)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match a in HtmlAttributeRegex().Matches(tag))
        {
            var value = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
            attributes.TryAdd(a.Groups[1].Value, System.Net.WebUtility.HtmlDecode(value));
        }
        if (!attributes.TryGetValue("src", out var src))
            return null;
        var source = sourceStart >= 0 ? new ImageSource(sourceStart, sourceStart + tag.Length, tag, true) : null;
        return ImageControl(src, attributes.GetValueOrDefault("alt", ""), attributes.GetValueOrDefault("title"), source,
            ImageLength.Parse(attributes.GetValueOrDefault("width"), Em), ImageLength.Parse(attributes.GetValueOrDefault("height"), Em));
    }

    private sealed record ImageSource(int Start, int End, string Original, bool IsHtml);

    /// <summary>Image arguments for a Markdown image: its source span and any Pandoc width/height attributes.</summary>
    private (ImageSource? Source, ImageLength? Width, ImageLength? Height) ImageDetails(LinkInline image)
    {
        ImageSource? source = null;
        if (image.Span.Start >= 0 && image.Span.Length > 0 && image.Span.End < _source.Length)
            source = new ImageSource(image.Span.Start, image.Span.End + 1, _source.Substring(image.Span.Start, image.Span.Length), false);
        var props = image.TryGetAttributes()?.Properties;
        string? Prop(string key) => props?.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        return (source, ImageLength.Parse(Prop("width"), Em), ImageLength.Parse(Prop("height"), Em));
    }

    [System.Text.RegularExpressions.GeneratedRegex("<[^>]+>")]
    private static partial System.Text.RegularExpressions.Regex HtmlTagRegex();

    private Control Figure(LinkInline image, BlockContext ctx)
    {
        var alt = PlainText(image);
        var panel = new StackPanel { Margin = new Thickness(0, Em * 0.3, 0, Math.Round(Em * 1.2)), Spacing = Math.Round(Em * 0.5) };
        var (source, width, height) = ImageDetails(image);
        var control = ImageControl(image.Url ?? "", alt, image.Title, source, width, height);
        control.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(control);
        var caption = !string.IsNullOrWhiteSpace(image.Title) ? image.Title : alt;
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var text = NewText(ctx with { Foreground = _muted }, Math.Round(Em * 0.85), 1.45);
            text.TextAlignment = TextAlignment.Center;
            text.FontStyle = FontStyle.Italic;
            text.Inlines!.Add(new Run(caption));
            panel.Children.Add(text);
        }
        return panel;
    }

    private Control ImageControl(string url, string alt, string? title, ImageSource? source, ImageLength? width, ImageLength? height)
    {
        var resolved = ImageLoader.Resolve(url, _s.BaseDirectory);
        var host = new Border { HorizontalAlignment = HorizontalAlignment.Left };

        Control Placeholder(string message) => new Border
        {
            BorderBrush = _rule,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            Child = new TextBlock
            {
                Text = message,
                FontFamily = _s.BodyFont,
                FontSize = Math.Round(Em * 0.8),
                Foreground = _muted,
                TextWrapping = TextWrapping.Wrap,
            },
        };

        if (resolved is null || !ImageLoader.IsSupported(resolved))
        {
            var reason = resolved is null && _s.BaseDirectory is null && !Uri.TryCreate(url, UriKind.Absolute, out _)
                ? "Save the document to show images with relative paths"
                : "Image";
            host.Child = Placeholder(string.IsNullOrEmpty(alt) ? reason : alt);
            if (title is { Length: > 0 })
                ToolTip.SetTip(host, title);
            return host;
        }

        var onResize = _onImageResize;
        void Apply(Bitmap? bitmap)
        {
            if (bitmap is null)
            {
                host.Child = Placeholder(string.IsNullOrEmpty(alt) ? "Image not found" : alt);
                return;
            }
            host.Child = new PreviewImage(bitmap, _t.Accent)
            {
                WidthSpec = width,
                HeightSpec = height,
                Tip = string.IsNullOrEmpty(title) ? null : title,
                Resized = source is null || onResize is null
                    ? null
                    : w => onResize(new ImageResize(source.Start, source.End, source.Original, source.IsHtml, w)),
            };
        }

        if (ImageLoader.TryGetCached(resolved, out var cached))
            Apply(cached);
        else
        {
            host.MinHeight = Em * 1.2;
            ImageLoader.LoadAsync(resolved).ContinueWith(t =>
            {
                host.MinHeight = 0;
                Apply(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        return host;
    }

    private static string PlainText(ContainerInline container)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in container.Descendants<LiteralInline>())
            sb.Append(inline.Content.ToString());
        return sb.ToString();
    }

    // ----------------------------------------------------------------- inlines

    private sealed class InlineSink(RichTextBlock block, InlineCollection target)
    {
        public RichTextBlock Block { get; } = block;
        public InlineCollection Target { get; set; } = target;
        public int Position { get; set; }
        public bool HasImages { get; set; }
    }

    private readonly record struct InlineStyle(
        double Size,
        double BaseSize,
        FontWeight? Weight,
        FontStyle? Style,
        IBrush? Foreground,
        FontFamily? Family,
        BaselineAlignment Baseline,
        bool Underline,
        bool Strike,
        bool DottedUnderline,
        IBrush? Background,
        IBrush? UnderlineBrush);

    private void BuildInlines(RichTextBlock block, ContainerInline? container, BlockContext ctx)
    {
        if (container is null)
            return;
        var sink = new InlineSink(block, block.Inlines!);
        var style = new InlineStyle(ctx.FontSize, ctx.FontSize, null, null, null, null, BaselineAlignment.Baseline, false, false, false, null, null);
        AddChildren(container, sink, style);
        if (sink.HasImages)
        {
            // A fixed line height would squeeze images into a single text line. Let lines grow to fit them and
            // keep the text spacing with extra line spacing instead.
            block.LineSpacing = Math.Max(0, block.LineHeight - _naturalLine * block.FontSize);
            block.LineHeight = double.NaN;
            block.RenderTransform = null;
        }
    }

    private void AddRun(InlineSink sink, InlineStyle st, string text)
    {
        if (text.Length == 0)
            return;
        var run = new Run(text);
        if (Math.Abs(st.Size - st.BaseSize) > 0.01)
            run.FontSize = st.Size;
        if (st.Weight is { } w)
            run.FontWeight = w;
        if (st.Style is { } s)
            run.FontStyle = s;
        if (st.Foreground is { } fg)
            run.Foreground = fg;
        if (st.Family is { } family)
            run.FontFamily = family;
        if (st.Baseline != BaselineAlignment.Baseline)
            run.BaselineAlignment = st.Baseline;
        if (st.Background is { } bg)
            run.Background = bg;
        if (st.Underline || st.Strike || st.DottedUnderline)
        {
            var decorations = new TextDecorationCollection();
            if (st.Underline)
            {
                decorations.Add(new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Stroke = st.UnderlineBrush,
                    StrokeThicknessUnit = TextDecorationUnit.FontRecommended,
                });
            }
            if (st.DottedUnderline)
            {
                decorations.Add(new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Stroke = _muted,
                    StrokeDashArray = new AvaloniaList<double> { 1, 2 },
                    StrokeThicknessUnit = TextDecorationUnit.FontRecommended,
                });
            }
            if (st.Strike)
                decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough, StrokeThicknessUnit = TextDecorationUnit.FontRecommended });
            run.TextDecorations = decorations;
        }
        sink.Target.Add(run);
        sink.Position += text.Length;
    }

    private void AddEmbedded(InlineSink sink, Control control)
    {
        sink.Target.Add(new InlineUIContainer(control));
        sink.Position += 1;
    }

    private void AddChildren(ContainerInline container, InlineSink sink, InlineStyle st)
    {
        // Inline HTML such as <sup>, <kbd> or <br> arrives as separate open/close tags between siblings,
        // so the active style is tracked on a stack while walking the sibling list.
        var htmlStack = new Stack<(string Tag, InlineStyle Previous)>();
        foreach (var inline in container)
        {
            if (inline is HtmlInline html)
            {
                var (tag, closing) = ParseTag(html.Tag);
                if (tag == "br")
                {
                    AddRun(sink, st, "\n");
                }
                else if (tag == "img" && HtmlImage(html.Tag, html.Span.Length == html.Tag.Length ? html.Span.Start : -1) is { } image)
                {
                    AddEmbedded(sink, image);
                    sink.HasImages = true;
                }
                else if (!closing && HtmlStyle(tag, st) is { } styled)
                {
                    htmlStack.Push((tag, st));
                    st = styled;
                }
                else if (closing && htmlStack.Any(e => e.Tag == tag))
                {
                    while (htmlStack.Count > 0)
                    {
                        var (open, previous) = htmlStack.Pop();
                        st = previous;
                        if (open == tag)
                            break;
                    }
                }
                continue;
            }
            AddInline(inline, sink, st);
        }
    }

    private static (string Tag, bool Closing) ParseTag(string raw)
    {
        var s = raw.Trim().TrimStart('<').TrimEnd('>').TrimEnd('/').Trim();
        var closing = s.StartsWith('/');
        if (closing)
            s = s[1..];
        var end = 0;
        while (end < s.Length && char.IsLetterOrDigit(s[end]))
            end++;
        return (s[..end].ToLowerInvariant(), closing);
    }

    private InlineStyle? HtmlStyle(string tag, InlineStyle st) => tag switch
    {
        "b" or "strong" => st with { Weight = FontWeight.Bold },
        "i" or "em" or "cite" or "dfn" or "var" => st with { Style = FontStyle.Italic },
        "u" or "ins" => st with { Underline = true },
        "s" or "del" or "strike" => st with { Strike = true },
        "mark" => st with { Background = _highlight },
        "sup" => st with { Baseline = BaselineAlignment.Superscript, Size = Math.Round(st.Size * 0.75) },
        "sub" => st with { Baseline = BaselineAlignment.Subscript, Size = Math.Round(st.Size * 0.75) },
        "small" => st with { Size = Math.Round(st.Size * 0.85) },
        "code" or "kbd" or "samp" or "tt" => st with { Family = _s.MonoFont, Size = Math.Round(st.Size * 0.86), Background = _codeBg },
        "span" or "abbr" or "a" => st,
        _ => null,
    };

    private void AddInline(MdInline inline, InlineSink sink, InlineStyle st)
    {
        switch (inline)
        {
            case TaskList:
                // Rendered as the list marker instead.
                break;

            case LiteralInline literal:
                AddRun(sink, st, literal.Content.ToString());
                break;

            case SmartyPant smarty:
                AddRun(sink, st, SmartText(smarty));
                break;

            case CodeInline code:
                AddRun(sink, st with
                {
                    Family = _s.MonoFont,
                    Size = Math.Round(st.Size * 0.86),
                    Background = _codeBg,
                }, code.Content);
                break;

            case LineBreakInline lineBreak:
                AddRun(sink, st, lineBreak.IsHard ? "\n" : " ");
                break;

            case HtmlEntityInline entity:
                AddRun(sink, st, entity.Transcoded.ToString());
                break;

            case MathInline math:
            {
                var element = new MathElement(math.Content.ToString().Trim(), Math.Round(st.Size * 1.04), _t.Foreground, ErrorColor, display: false);
                AddEmbedded(sink, element);
                break;
            }

            case AutolinkInline autolink:
            {
                var url = autolink.IsEmail && !autolink.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    ? "mailto:" + autolink.Url
                    : autolink.Url;
                var start = sink.Position;
                AddRun(sink, LinkStyle(st), autolink.Url);
                sink.Block.Ranges.Add(new InteractiveRange(start, sink.Position, url, url));
                break;
            }

            case LinkInline { IsImage: true } image:
            {
                var (source, width, height) = ImageDetails(image);
                AddEmbedded(sink, ImageControl(image.Url ?? "", PlainText(image), image.Title, source, width, height));
                sink.HasImages = true;
                break;
            }

            case LinkInline link:
            {
                var url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? "";
                var start = sink.Position;
                AddChildren(link, sink, LinkStyle(st));
                var tip = string.IsNullOrEmpty(link.Title) ? url : link.Title + "\n" + url;
                sink.Block.Ranges.Add(new InteractiveRange(start, sink.Position, url, tip));
                break;
            }

            case EmphasisInline emphasis:
                AddChildren(emphasis, sink, EmphasisStyle(emphasis, st));
                break;

            case FootnoteLink { IsBackLink: true } back:
            {
                var start = sink.Position;
                AddRun(sink, st with { Foreground = _accent, Family = FontFamily.Default }, " ↩");
                sink.Block.Ranges.Add(new InteractiveRange(start, sink.Position, "#fnref:" + back.Footnote.Order, "Back to text"));
                break;
            }

            case FootnoteLink footnoteLink:
            {
                var order = footnoteLink.Footnote.Order;
                _anchors.TryAdd("fnref:" + order, sink.Block);
                var start = sink.Position;
                AddRun(sink, st with
                {
                    Foreground = _accent,
                    Baseline = BaselineAlignment.Superscript,
                    Size = Math.Round(st.Size * 0.72),
                    Weight = FontWeight.SemiBold,
                }, order.ToString());
                sink.Block.Ranges.Add(new InteractiveRange(start, sink.Position, "#fn:" + order, FootnotePreview(footnoteLink.Footnote)));
                break;
            }

            case AbbreviationInline abbreviation:
            {
                var start = sink.Position;
                var label = abbreviation.Abbreviation.Label ?? "";
                AddRun(sink, st with { DottedUnderline = true }, label);
                sink.Block.Ranges.Add(new InteractiveRange(start, sink.Position, null, abbreviation.Abbreviation.Text.ToString()));
                break;
            }

            case DelimiterInline delimiter:
                AddRun(sink, st, delimiter.ToLiteral());
                break;

            case ContainerInline containerInline:
                AddChildren(containerInline, sink, st);
                break;

            case LeafInline leaf:
                AddRun(sink, st, leaf.ToString() ?? "");
                break;
        }
    }

    private static string FootnotePreview(Footnote footnote)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var paragraph in footnote.Descendants<ParagraphBlock>())
        {
            if (paragraph.Inline is null)
                continue;
            foreach (var inline in paragraph.Inline.Descendants<MdInline>())
            {
                switch (inline)
                {
                    case LiteralInline l:
                        sb.Append(l.Content.ToString());
                        break;
                    case CodeInline c:
                        sb.Append(c.Content);
                        break;
                    case LineBreakInline:
                        sb.Append(' ');
                        break;
                    case SmartyPant s:
                        sb.Append(SmartText(s));
                        break;
                }
            }
            sb.Append(' ');
        }
        var text = sb.ToString().Trim();
        return text.Length > 300 ? text[..300] + "…" : text;
    }

    private InlineStyle LinkStyle(InlineStyle st) => st with { Foreground = _accent, Underline = true, UnderlineBrush = _linkUnderline };

    private InlineStyle EmphasisStyle(EmphasisInline emphasis, InlineStyle st) => (emphasis.DelimiterChar, emphasis.DelimiterCount) switch
    {
        ('*' or '_', 1) => st with { Style = st.Style == FontStyle.Italic ? FontStyle.Normal : FontStyle.Italic },
        ('*' or '_', _) => st with { Weight = FontWeight.Bold },
        ('~', 1) => st with { Baseline = BaselineAlignment.Subscript, Size = Math.Round(st.Size * 0.75) },
        ('~', _) => st with { Strike = true, Foreground = st.Foreground ?? _muted },
        ('^', _) => st with { Baseline = BaselineAlignment.Superscript, Size = Math.Round(st.Size * 0.75) },
        ('+', _) => st with { Underline = true },
        ('=', _) => st with { Background = _highlight },
        _ => st,
    };

    private static string SmartText(SmartyPant smarty) => smarty.Type switch
    {
        SmartyPantType.Quote => "’",
        SmartyPantType.LeftQuote => "‘",
        SmartyPantType.RightQuote => "’",
        SmartyPantType.LeftDoubleQuote => "“",
        SmartyPantType.RightDoubleQuote => "”",
        SmartyPantType.LeftAngleQuote => "«",
        SmartyPantType.RightAngleQuote => "»",
        SmartyPantType.Ellipsis => "…",
        SmartyPantType.Dash2 => "–",
        SmartyPantType.Dash3 => "—",
        _ => smarty.ToString() ?? "",
    };
}
