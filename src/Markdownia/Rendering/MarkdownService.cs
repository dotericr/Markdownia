using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace Markdownia.Rendering;

/// <summary>Document title block, from YAML front matter or a Pandoc "% title" block.</summary>
public sealed record DocumentMetadata(string? Title, string? Subtitle, IReadOnlyList<string> Authors, string? Date)
{
    public bool IsEmpty => Title is null && Subtitle is null && Authors.Count == 0 && Date is null;
}

/// <param name="Source">The parsed text. Syntax-tree source spans are offsets into it.</param>
public sealed record ParsedDocument(MarkdownDocument Document, DocumentMetadata Metadata, string Source);

/// <summary>
/// Parses Markdown with a pipeline combining GitHub Flavored Markdown with the Pandoc extensions Markdig supports
/// (footnotes, super/subscript, abbreviations, definition lists, fenced divs, grid tables, fancy lists, math,
/// attributes, title blocks).
/// </summary>
public static class MarkdownService
{
    private static readonly MarkdownPipeline PlainPipeline = Build(smartTypography: false);
    private static readonly MarkdownPipeline SmartPipeline = Build(smartTypography: true);

    private static MarkdownPipeline Build(bool smartTypography)
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UseYamlFrontMatter()
            .UseAlertBlocks()
            .UseAbbreviations()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseCustomContainers()
            .UseDefinitionLists()
            .UseEmphasisExtras(EmphasisExtraOptions.Default)
            .UseFootnotes()
            .UseGridTables()
            .UsePipeTables()
            .UseListExtras()
            .UseTaskLists()
            .UseMathematics()
            .UseAutoLinks()
            .UseEmojiAndSmiley(enableSmileys: false);
        if (smartTypography)
            builder.UseSmartyPants();
        // Generic attributes must be registered last.
        builder.UseGenericAttributes();
        return builder.Build();
    }

    public static MarkdownPipeline Pipeline(bool smartTypography) => smartTypography ? SmartPipeline : PlainPipeline;

    public static ParsedDocument Parse(string text, bool smartTypography)
    {
        var source = text;
        var pandocTitle = ExtractPandocTitleBlock(ref text);
        var document = Markdown.Parse(text, Pipeline(smartTypography));
        var metadata = pandocTitle ?? ReadFrontMatter(document) ?? new DocumentMetadata(null, null, [], null);
        return new ParsedDocument(document, metadata, source);
    }

    /// <summary>
    /// Pandoc title block: up to three leading lines beginning with '%' (title, author(s), date).
    /// The lines are replaced with spaces (not removed) so source lines and offsets stay aligned with the editor.
    /// </summary>
    private static DocumentMetadata? ExtractPandocTitleBlock(ref string text)
    {
        if (!text.StartsWith('%'))
            return null;
        var lines = text.Split('\n');
        var values = new List<string>();
        var i = 0;
        for (; i < lines.Length && values.Count < 3; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (!line.StartsWith('%'))
                break;
            values.Add(line[1..].Trim());
            lines[i] = new string(' ', line.Length) + lines[i][line.Length..];
        }
        text = string.Join('\n', lines);
        string? Get(int index) => index < values.Count && values[index].Length > 0 ? values[index] : null;
        var authors = Get(1)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return new DocumentMetadata(Get(0), null, authors, Get(2));
    }

    private static DocumentMetadata? ReadFrontMatter(MarkdownDocument document)
    {
        var yaml = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (yaml is null)
            return null;

        string? title = null, subtitle = null, date = null;
        var authors = new List<string>();
        string? currentListKey = null;
        foreach (var raw in yaml.Lines.Lines.Take(yaml.Lines.Count).Select(l => l.ToString()))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ") && currentListKey == "author")
            {
                var value = Unquote(trimmed[2..]);
                if (value.StartsWith("name:"))
                    value = Unquote(value[5..]);
                authors.Add(value);
                continue;
            }
            if (line.StartsWith(' ') && currentListKey == "author" && trimmed.StartsWith("name:"))
            {
                authors.Add(Unquote(trimmed[5..]));
                continue;
            }
            var colon = line.IndexOf(':');
            if (colon <= 0 || line.StartsWith(' '))
                continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var val = Unquote(line[(colon + 1)..]);
            currentListKey = val.Length == 0 ? key : null;
            switch (key)
            {
                case "title": title = NullIfEmpty(val); break;
                case "subtitle": subtitle = NullIfEmpty(val); break;
                case "date": date = NullIfEmpty(val); break;
                case "author" or "authors" when val.Length > 0:
                    authors.AddRange(val.Trim('[', ']').Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote));
                    break;
                case "authors":
                    currentListKey = "author";
                    break;
            }
        }
        var meta = new DocumentMetadata(title, subtitle, authors, date);
        return meta.IsEmpty ? null : meta;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && (s[0] == '"' && s[^1] == '"' || s[0] == '\'' && s[^1] == '\''))
            s = s[1..^1];
        return s;
    }

    public static string ToHtml(string text, bool smartTypography) =>
        Markdown.ToHtml(text, Pipeline(smartTypography));
}
