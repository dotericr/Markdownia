using Markdownia.Rendering;

namespace Markdownia.Tests;

public class HighlighterTests
{
    private static List<(string Text, TokenKind Kind)> Tokens(string code, string lang) =>
        SyntaxHighlighter.Tokenize(code, lang).Select(t => (code.Substring(t.Start, t.Length), t.Kind)).ToList();

    [Fact]
    public void Tokens_cover_the_whole_input_without_gaps()
    {
        const string code = "int x = 42; // answer\nstring s = \"hi\";";
        var tokens = SyntaxHighlighter.Tokenize(code, "csharp");
        var pos = 0;
        foreach (var t in tokens)
        {
            Assert.Equal(pos, t.Start);
            pos += t.Length;
        }
        Assert.Equal(code.Length, pos);
    }

    [Fact]
    public void CSharp_keywords_strings_numbers_and_comments()
    {
        var tokens = Tokens("var s = $\"x{1}\"; // done\nreturn 0x1F;", "cs");
        Assert.Contains(("var", TokenKind.Keyword), tokens);
        Assert.Contains(("$\"x{1}\"", TokenKind.String), tokens);
        Assert.Contains(("// done", TokenKind.Comment), tokens);
        Assert.Contains(("return", TokenKind.Keyword), tokens);
        Assert.Contains(("0x1F", TokenKind.Number), tokens);
    }

    [Fact]
    public void Python_triple_quotes_and_decorators()
    {
        var tokens = Tokens("@cache\ndef f():\n    \"\"\"doc\nmore\"\"\"\n    return None", "py");
        Assert.Contains(("@cache", TokenKind.Preprocessor), tokens);
        Assert.Contains(("\"\"\"doc\nmore\"\"\"", TokenKind.String), tokens);
        Assert.Contains(("None", TokenKind.Number), tokens);
        Assert.Contains(("f", TokenKind.Function), tokens);
    }

    [Fact]
    public void Json_keys_are_properties()
    {
        var tokens = Tokens("{ \"name\": \"value\", \"n\": 1 }", "json");
        Assert.Contains(("\"name\"", TokenKind.Property), tokens);
        Assert.Contains(("\"value\"", TokenKind.String), tokens);
    }

    [Fact]
    public void Unknown_language_is_plain()
    {
        var tokens = SyntaxHighlighter.Tokenize("whatever", "klingon");
        Assert.Equal(TokenKind.Plain, Assert.Single(tokens).Kind);
    }
}
