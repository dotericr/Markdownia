using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdownia.Rendering;

namespace Markdownia.Tests;

public class ParsingTests
{
    private static MarkdownDocument Parse(string text) => MarkdownService.Parse(text, smartTypography: true).Document;

    [Fact]
    public void Pandoc_superscript_subscript_and_gfm_strikethrough_coexist()
    {
        var doc = Parse("H~2~O and x^2^ and ~~gone~~");
        var emphasis = doc.Descendants<EmphasisInline>().Select(e => (e.DelimiterChar, e.DelimiterCount)).ToList();
        Assert.Contains(('~', 1), emphasis);
        Assert.Contains(('^', 1), emphasis);
        Assert.Contains(('~', 2), emphasis);
    }

    [Fact]
    public void Footnotes_abbreviations_and_definition_lists_parse()
    {
        var doc = Parse("Term\n:   Definition\n\nUses HTML.[^n]\n\n*[HTML]: HyperText Markup Language\n\n[^n]: A note.");
        Assert.Single(doc.OfType<FootnoteGroup>());
        Assert.Single(doc.Descendants<AbbreviationInline>());
        Assert.Single(doc.Descendants<DefinitionList>());
    }

    [Fact]
    public void Gfm_tables_task_lists_and_alerts_parse()
    {
        var doc = Parse("| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] done\n\n> [!WARNING]\n> Careful");
        Assert.Single(doc.Descendants<Table>());
        Assert.Single(doc.Descendants<TaskList>());
        Assert.Single(doc.Descendants<AlertBlock>());
    }

    [Fact]
    public void Math_and_fenced_divs_parse()
    {
        var doc = Parse("Inline $x^2$.\n\n$$\n\\frac{1}{2}\n$$\n\n::: note\nText\n:::");
        Assert.Single(doc.Descendants<MathInline>());
        Assert.Single(doc.Descendants<MathBlock>());
        Assert.Single(doc.Descendants<CustomContainer>());
    }

    [Fact]
    public void Yaml_front_matter_becomes_title_block()
    {
        var parsed = MarkdownService.Parse("---\ntitle: \"My Doc\"\nauthor:\n  - Ada\n  - Grace\ndate: 2026\n---\n\nBody", smartTypography: false);
        Assert.Equal("My Doc", parsed.Metadata.Title);
        Assert.Equal(["Ada", "Grace"], parsed.Metadata.Authors);
        Assert.Equal("2026", parsed.Metadata.Date);
    }

    [Fact]
    public void Pandoc_percent_title_block_keeps_line_numbers()
    {
        var parsed = MarkdownService.Parse("% The Title\n% Author One; Author Two\n% June 2026\n\n# Heading", smartTypography: false);
        Assert.Equal("The Title", parsed.Metadata.Title);
        Assert.Equal(2, parsed.Metadata.Authors.Count);
        Assert.Equal(4, parsed.Document.Descendants<HeadingBlock>().Single().Line);
    }
}
