using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using Markdownia.Theming;

namespace Markdownia.Editing;

/// <summary>
/// Markdown syntax highlighting for the editor, built in code rather than loaded from XSHD so that colors come
/// straight from the active theme (and no XML loader is needed at runtime).
/// </summary>
public sealed class MarkdownHighlighting : IHighlightingDefinition
{
    private readonly Dictionary<string, HighlightingColor> _colors = new(StringComparer.Ordinal);

    private readonly HighlightingColor _heading = new() { Name = "Heading", FontWeight = FontWeight.Bold };
    private readonly HighlightingColor _emphasis = new() { Name = "Emphasis", FontStyle = FontStyle.Italic };
    private readonly HighlightingColor _strong = new() { Name = "Strong", FontWeight = FontWeight.Bold };
    private readonly HighlightingColor _strike = new() { Name = "Strike", Strikethrough = true };
    private readonly HighlightingColor _code = new() { Name = "Code" };
    private readonly HighlightingColor _fence = new() { Name = "Fence" };
    private readonly HighlightingColor _link = new() { Name = "Link" };
    private readonly HighlightingColor _url = new() { Name = "Url" };
    private readonly HighlightingColor _quote = new() { Name = "Quote" };
    private readonly HighlightingColor _marker = new() { Name = "ListMarker", FontWeight = FontWeight.Bold };
    private readonly HighlightingColor _math = new() { Name = "Math" };
    private readonly HighlightingColor _comment = new() { Name = "Comment", FontStyle = FontStyle.Italic };
    private readonly HighlightingColor _html = new() { Name = "Html" };
    private readonly HighlightingColor _rule = new() { Name = "Rule" };
    private readonly HighlightingColor _mark = new() { Name = "Mark" };
    private readonly HighlightingColor _footnote = new() { Name = "Footnote" };

    public MarkdownHighlighting(MarkdownTheme theme)
    {
        foreach (var c in new[] { _heading, _emphasis, _strong, _strike, _code, _fence, _link, _url, _quote, _marker, _math, _comment, _html, _rule, _mark, _footnote })
            _colors[c.Name] = c;

        MainRuleSet = BuildRuleSet();
        ApplyTheme(theme);
    }

    public string Name => "Markdown";

    public HighlightingRuleSet MainRuleSet { get; }

    public IEnumerable<HighlightingColor> NamedHighlightingColors => _colors.Values;

    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

    public HighlightingRuleSet GetNamedRuleSet(string name) => MainRuleSet;

    public HighlightingColor GetNamedColor(string name) => _colors.GetValueOrDefault(name)!;

    public void ApplyTheme(MarkdownTheme theme)
    {
        static HighlightingBrush B(Color c) => new SimpleHighlightingBrush(c);
        var s = theme.Syntax;
        _heading.Foreground = B(theme.Accent);
        _emphasis.Foreground = null;
        _strong.Foreground = null;
        _strike.Foreground = B(theme.Muted);
        _code.Foreground = B(s.String);
        _fence.Foreground = B(s.String);
        _link.Foreground = B(theme.Accent);
        _url.Foreground = B(theme.Muted);
        _quote.Foreground = B(theme.Muted);
        _marker.Foreground = B(s.Keyword);
        _math.Foreground = B(s.Number);
        _comment.Foreground = B(s.Comment);
        _html.Foreground = B(s.Preprocessor);
        _rule.Foreground = B(theme.Muted);
        _mark.Background = B(Color.FromArgb(theme.IsDark ? (byte)110 : (byte)150, theme.Highlight.R, theme.Highlight.G, theme.Highlight.B));
        _footnote.Foreground = B(s.Function);
    }

    private static Regex R(string pattern) => new(pattern, RegexOptions.CultureInvariant);

    private HighlightingRuleSet BuildRuleSet()
    {
        var main = new HighlightingRuleSet { Name = "Markdown" };
        var plain = new HighlightingRuleSet { Name = "Plain" };

        main.Spans.Add(new HighlightingSpan
        {
            StartExpression = R(@"^[ \t]{0,3}(`{3,}|~{3,})"),
            EndExpression = R(@"^[ \t]{0,3}(`{3,}|~{3,})[ \t]*$"),
            SpanColor = _fence,
            SpanColorIncludesStart = true,
            SpanColorIncludesEnd = true,
            RuleSet = plain,
        });
        main.Spans.Add(new HighlightingSpan
        {
            StartExpression = R(@"\$\$"),
            EndExpression = R(@"\$\$"),
            SpanColor = _math,
            SpanColorIncludesStart = true,
            SpanColorIncludesEnd = true,
            RuleSet = plain,
        });
        main.Spans.Add(new HighlightingSpan
        {
            StartExpression = R("<!--"),
            EndExpression = R("-->"),
            SpanColor = _comment,
            SpanColorIncludesStart = true,
            SpanColorIncludesEnd = true,
            RuleSet = plain,
        });

        void Rule(string pattern, HighlightingColor color) => main.Rules.Add(new HighlightingRule { Regex = R(pattern), Color = color });

        Rule(@"^[ \t]{0,3}#{1,6}([ \t].*)?$", _heading);
        Rule(@"^[ \t]{0,3}(=+|-{2,})[ \t]*$", _rule);
        Rule(@"^[ \t]{0,3}([-*_][ \t]*){3,}$", _rule);
        Rule(@"^[ \t]{0,3}(%.*)$", _heading);
        Rule(@"^[ \t]*(>[ \t]?)+", _quote);
        Rule(@"^[ \t]*([-+*]|\d{1,9}[.)]|[a-zA-Z][.)]|[ivxIVX]+[.)])(?=[ \t])", _marker);
        Rule(@"^[ \t]*:(?=[ \t])", _marker);
        Rule(@"(?<=^[ \t]*[-+*][ \t]+)\[[ xX]\]", _marker);
        Rule(@"`+[^`\n]+`+", _code);
        Rule(@"(?<![\\$])\$(?=\S)[^$\n]*?(?<=\S)\$(?!\d)", _math);
        Rule(@"!?\[\^[^\]\n]+\]:?", _footnote);
        Rule(@"!?\[[^\]\n]*\](?=[(\[])", _link);
        Rule(@"(?<=\])\([^)\n]*\)", _url);
        Rule(@"(?<=\])\[[^\]\n]*\]", _url);
        Rule(@"^[ \t]{0,3}\[[^\]\n]+\]:[ \t].*$", _url);
        Rule(@"^\*\[[^\]\n]+\]:.*$", _footnote);
        Rule(@"<(https?|mailto|ftp):[^>\s]+>", _link);
        Rule(@"(?<![\w(<])https?://[^\s<>)\]]+", _link);
        Rule(@"</?[a-zA-Z][a-zA-Z0-9-]*(\s[^>\n]*)?/?>", _html);
        Rule(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", _strong);
        Rule(@"(?<![*\w\\])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![*\w])", _emphasis);
        Rule(@"(?<![_\w\\])_(?=[^\s_])(.+?)(?<=[^\s_])_(?![_\w])", _emphasis);
        Rule(@"~~(?=\S)(.+?)(?<=\S)~~", _strike);
        Rule(@"==(?=\S)(.+?)(?<=\S)==", _mark);
        Rule(@"(?<!\\)[\^~](?=\S)[^\s^~]+(?<=\S)[\^~]", _math);
        return main;
    }
}
