using Avalonia.Media;

namespace Markdownia.Theming;

/// <summary>Colors used to highlight code, both in preview code blocks and in the editor.</summary>
public sealed record SyntaxPalette(
    Color Keyword,
    Color Type,
    Color String,
    Color Number,
    Color Comment,
    Color Preprocessor,
    Color Function,
    Color Property);

/// <summary>A complete color scheme for the application chrome, editor and preview.</summary>
public sealed record MarkdownTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsDark { get; init; }

    // Preview page
    public required Color Background { get; init; }
    public required Color Foreground { get; init; }
    public required Color Heading { get; init; }
    public required Color Muted { get; init; }
    public required Color Accent { get; init; }
    public required Color Rule { get; init; }
    public required Color CodeBackground { get; init; }
    public required Color CodeBorder { get; init; }
    public required Color QuoteBar { get; init; }
    public required Color Highlight { get; init; }
    public required Color TableHeader { get; init; }
    public required Color TableStripe { get; init; }
    public required Color Selection { get; init; }

    // Application chrome
    public required Color Chrome { get; init; }
    public required Color ChromeBorder { get; init; }
    public required Color ChromeForeground { get; init; }

    // Editor
    public required Color EditorBackground { get; init; }
    public required Color EditorForeground { get; init; }
    public required Color EditorLineNumbers { get; init; }
    public required Color EditorCurrentLine { get; init; }

    public required SyntaxPalette Syntax { get; init; }

    public Color CodeForeground => Foreground;
}

public static class Themes
{
    private static Color C(string hex) => Color.Parse(hex);

    public static readonly MarkdownTheme Paper = new()
    {
        Id = "paper", Name = "Paper", IsDark = false,
        Background = C("#FBFAF6"), Foreground = C("#2B2926"), Heading = C("#1A1816"), Muted = C("#6E6960"),
        Accent = C("#1D5FA8"), Rule = C("#E2DDD2"), CodeBackground = C("#F3F0E8"), CodeBorder = C("#E5E0D4"),
        QuoteBar = C("#D4CBB8"), Highlight = C("#FAE7A0"), TableHeader = C("#F1EEE6"), TableStripe = C("#F7F5EF"),
        Selection = C("#C9DCF2"),
        Chrome = C("#F2F0EA"), ChromeBorder = C("#DEDAD0"), ChromeForeground = C("#2B2926"),
        EditorBackground = C("#FFFFFF"), EditorForeground = C("#2B2926"), EditorLineNumbers = C("#B3ADA2"), EditorCurrentLine = C("#F6F4EE"),
        Syntax = new(C("#A626A4"), C("#B76B01"), C("#3E8B3B"), C("#986801"), C("#9A9890"), C("#C2410C"), C("#2F62C9"), C("#B0331F")),
    };

    public static readonly MarkdownTheme GitHubLight = new()
    {
        Id = "github-light", Name = "GitHub Light", IsDark = false,
        Background = C("#FFFFFF"), Foreground = C("#1F2328"), Heading = C("#1F2328"), Muted = C("#59636E"),
        Accent = C("#0969DA"), Rule = C("#D1D9E0"), CodeBackground = C("#F6F8FA"), CodeBorder = C("#D1D9E0"),
        QuoteBar = C("#D1D9E0"), Highlight = C("#FFF3B0"), TableHeader = C("#F6F8FA"), TableStripe = C("#FAFBFC"),
        Selection = C("#B6D7FF"),
        Chrome = C("#F6F8FA"), ChromeBorder = C("#D1D9E0"), ChromeForeground = C("#1F2328"),
        EditorBackground = C("#FFFFFF"), EditorForeground = C("#1F2328"), EditorLineNumbers = C("#A0A8B2"), EditorCurrentLine = C("#F6F8FA"),
        Syntax = new(C("#CF222E"), C("#953800"), C("#0A3069"), C("#0550AE"), C("#6E7781"), C("#8250DF"), C("#8250DF"), C("#116329")),
    };

    public static readonly MarkdownTheme Sepia = new()
    {
        Id = "sepia", Name = "Sepia", IsDark = false,
        Background = C("#F5EDDC"), Foreground = C("#3F3020"), Heading = C("#33251A"), Muted = C("#7D6A54"),
        Accent = C("#9C4A1A"), Rule = C("#DCCCAD"), CodeBackground = C("#EDE3CC"), CodeBorder = C("#DCCCAD"),
        QuoteBar = C("#C8B28A"), Highlight = C("#F1D68A"), TableHeader = C("#ECE1C8"), TableStripe = C("#F1E8D4"),
        Selection = C("#E2CDA0"),
        Chrome = C("#EBE1CB"), ChromeBorder = C("#D6C6A6"), ChromeForeground = C("#3F3020"),
        EditorBackground = C("#FAF4E8"), EditorForeground = C("#3F3020"), EditorLineNumbers = C("#B3A285"), EditorCurrentLine = C("#F2E9D6"),
        Syntax = new(C("#9B2D30"), C("#8A5A00"), C("#4F6B2A"), C("#A0522D"), C("#9A8970"), C("#7A4E9C"), C("#2F5D8A"), C("#8C3B2E")),
    };

    public static readonly MarkdownTheme SolarizedLight = new()
    {
        Id = "solarized-light", Name = "Solarized Light", IsDark = false,
        Background = C("#FDF6E3"), Foreground = C("#4A5A60"), Heading = C("#073642"), Muted = C("#839496"),
        Accent = C("#268BD2"), Rule = C("#E4DCC4"), CodeBackground = C("#EEE8D5"), CodeBorder = C("#E1D9C1"),
        QuoteBar = C("#B5BFB8"), Highlight = C("#F6E2A2"), TableHeader = C("#EEE8D5"), TableStripe = C("#F7F0DC"),
        Selection = C("#E6DDBF"),
        Chrome = C("#EEE8D5"), ChromeBorder = C("#DDD5BC"), ChromeForeground = C("#073642"),
        EditorBackground = C("#FDF6E3"), EditorForeground = C("#4A5A60"), EditorLineNumbers = C("#A9B3B2"), EditorCurrentLine = C("#F5EEDA"),
        Syntax = new(C("#859900"), C("#B58900"), C("#2AA198"), C("#D33682"), C("#93A1A1"), C("#CB4B16"), C("#268BD2"), C("#6C71C4")),
    };

    public static readonly MarkdownTheme GitHubDark = new()
    {
        Id = "github-dark", Name = "GitHub Dark", IsDark = true,
        Background = C("#0D1117"), Foreground = C("#D5DCE4"), Heading = C("#F0F6FC"), Muted = C("#9198A1"),
        Accent = C("#58A6FF"), Rule = C("#30363D"), CodeBackground = C("#151B23"), CodeBorder = C("#30363D"),
        QuoteBar = C("#3D444D"), Highlight = C("#5C4813"), TableHeader = C("#151B23"), TableStripe = C("#11161D"),
        Selection = C("#264F78"),
        Chrome = C("#161B22"), ChromeBorder = C("#30363D"), ChromeForeground = C("#E6EDF3"),
        EditorBackground = C("#0D1117"), EditorForeground = C("#D5DCE4"), EditorLineNumbers = C("#4E5762"), EditorCurrentLine = C("#151B23"),
        Syntax = new(C("#FF7B72"), C("#FFA657"), C("#A5D6FF"), C("#79C0FF"), C("#8B949E"), C("#FF7B72"), C("#D2A8FF"), C("#7EE787")),
    };

    public static readonly MarkdownTheme Nord = new()
    {
        Id = "nord", Name = "Nord", IsDark = true,
        Background = C("#2E3440"), Foreground = C("#D8DEE9"), Heading = C("#ECEFF4"), Muted = C("#9AA3B5"),
        Accent = C("#88C0D0"), Rule = C("#434C5E"), CodeBackground = C("#3B4252"), CodeBorder = C("#434C5E"),
        QuoteBar = C("#5E81AC"), Highlight = C("#6B5F3E"), TableHeader = C("#3B4252"), TableStripe = C("#333A47"),
        Selection = C("#4C566A"),
        Chrome = C("#272C36"), ChromeBorder = C("#3B4252"), ChromeForeground = C("#E5E9F0"),
        EditorBackground = C("#2E3440"), EditorForeground = C("#D8DEE9"), EditorLineNumbers = C("#5E6779"), EditorCurrentLine = C("#353C4A"),
        Syntax = new(C("#81A1C1"), C("#8FBCBB"), C("#A3BE8C"), C("#B48EAD"), C("#7B88A1"), C("#D08770"), C("#88C0D0"), C("#EBCB8B")),
    };

    public static readonly MarkdownTheme OneDark = new()
    {
        Id = "one-dark", Name = "One Dark", IsDark = true,
        Background = C("#282C34"), Foreground = C("#C8CDD5"), Heading = C("#E8EBF0"), Muted = C("#8B929E"),
        Accent = C("#61AFEF"), Rule = C("#3E4451"), CodeBackground = C("#21252B"), CodeBorder = C("#3A3F4B"),
        QuoteBar = C("#4B5263"), Highlight = C("#5E5130"), TableHeader = C("#2F343E"), TableStripe = C("#2B3039"),
        Selection = C("#3E4451"),
        Chrome = C("#21252B"), ChromeBorder = C("#181A1F"), ChromeForeground = C("#D7DAE0"),
        EditorBackground = C("#282C34"), EditorForeground = C("#C8CDD5"), EditorLineNumbers = C("#4B5263"), EditorCurrentLine = C("#2C313C"),
        Syntax = new(C("#C678DD"), C("#E5C07B"), C("#98C379"), C("#D19A66"), C("#7F848E"), C("#E06C75"), C("#61AFEF"), C("#E06C75")),
    };

    public static readonly MarkdownTheme SolarizedDark = new()
    {
        Id = "solarized-dark", Name = "Solarized Dark", IsDark = true,
        Background = C("#002B36"), Foreground = C("#A3B1B1"), Heading = C("#EEE8D5"), Muted = C("#7A8C8E"),
        Accent = C("#3A9BE0"), Rule = C("#0D4351"), CodeBackground = C("#073642"), CodeBorder = C("#0D4351"),
        QuoteBar = C("#2D5F6B"), Highlight = C("#4D4A1A"), TableHeader = C("#073642"), TableStripe = C("#04313C"),
        Selection = C("#0E4A59"),
        Chrome = C("#00232C"), ChromeBorder = C("#073642"), ChromeForeground = C("#EEE8D5"),
        EditorBackground = C("#002B36"), EditorForeground = C("#A3B1B1"), EditorLineNumbers = C("#3B5F68"), EditorCurrentLine = C("#04323E"),
        Syntax = new(C("#859900"), C("#B58900"), C("#2AA198"), C("#D33682"), C("#6C8389"), C("#CB4B16"), C("#3A9BE0"), C("#6C71C4")),
    };

    public static readonly MarkdownTheme Dracula = new()
    {
        Id = "dracula", Name = "Dracula", IsDark = true,
        Background = C("#282A36"), Foreground = C("#E4E4DE"), Heading = C("#F8F8F2"), Muted = C("#9AA2C8"),
        Accent = C("#8BE9FD"), Rule = C("#44475A"), CodeBackground = C("#21222C"), CodeBorder = C("#3B3E51"),
        QuoteBar = C("#6272A4"), Highlight = C("#5A5532"), TableHeader = C("#303241"), TableStripe = C("#2B2D3A"),
        Selection = C("#44475A"),
        Chrome = C("#21222C"), ChromeBorder = C("#191A21"), ChromeForeground = C("#F8F8F2"),
        EditorBackground = C("#282A36"), EditorForeground = C("#E4E4DE"), EditorLineNumbers = C("#5A5F7D"), EditorCurrentLine = C("#2F3140"),
        Syntax = new(C("#FF79C6"), C("#8BE9FD"), C("#F1FA8C"), C("#BD93F9"), C("#7B88BD"), C("#FFB86C"), C("#50FA7B"), C("#FFB86C")),
    };

    public static readonly IReadOnlyList<MarkdownTheme> All =
        [Paper, GitHubLight, Sepia, SolarizedLight, GitHubDark, Nord, OneDark, SolarizedDark, Dracula];

    public static MarkdownTheme Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? Paper;
}
