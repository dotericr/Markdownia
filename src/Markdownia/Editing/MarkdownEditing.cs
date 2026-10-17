using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace Markdownia.Editing;

public enum LineFormat
{
    Bullet,
    Numbered,
    Task,
    Quote,
}

/// <summary>Markdown-aware editing operations used by the ribbon and keyboard shortcuts.</summary>
public sealed partial class MarkdownEditing(TextEditor editor)
{
    private TextDocument Doc => editor.Document;

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-+*]|\d{1,9}[.)])(?<space>[ \t]+)(?<task>\[[ xX]\][ \t]+)?")]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"^(?<indent>[ \t]*)>[ \t]?")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^[ ]{0,3}(?<hashes>#{1,6})(?:[ \t]+|$)")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\[\^(?<n>\d+)\]")]
    private static partial Regex FootnoteRefRegex();

    [GeneratedRegex(@"\s+(?:width|height)\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlSizeAttributeRegex();

    /// <summary>
    /// Computes the edit that sets (or, with a null width, removes) an image's width: a Pandoc attribute block
    /// <c>{width=300}</c> after a Markdown image, or a <c>width="300"</c> attribute on an HTML <c>&lt;img&gt;</c>.
    /// Any height is removed so the image keeps its aspect ratio. Returns null when nothing changes.
    /// </summary>
    public static (int Offset, int Length, string Text)? ImageWidthEdit(string text, int start, int end, bool isHtml, int? width)
    {
        if (isHtml)
        {
            var tag = text[start..end];
            var stripped = HtmlSizeAttributeRegex().Replace(tag, "");
            if (width is { } w)
            {
                var close = stripped.EndsWith("/>", StringComparison.Ordinal) ? stripped.Length - 2 : stripped.Length - 1;
                var head = stripped[..close].TrimEnd();
                stripped = head + $" width=\"{w}\"" + (close == stripped.Length - 2 ? " />" : ">");
            }
            return stripped == tag ? null : (start, end - start, stripped);
        }

        // Pandoc attributes directly follow the image: ![alt](src){#id .class width=300}
        var blockEnd = -1;
        if (end < text.Length && text[end] == '{')
        {
            var close = text.IndexOf('}', end);
            var newline = text.IndexOfAny(['\r', '\n'], end);
            if (close > 0 && (newline < 0 || close < newline))
                blockEnd = close + 1;
        }
        var tokens = blockEnd > 0 ? SplitAttributes(text[(end + 1)..(blockEnd - 1)]) : [];
        tokens.RemoveAll(t => t.StartsWith("width=", StringComparison.OrdinalIgnoreCase) || t.StartsWith("height=", StringComparison.OrdinalIgnoreCase));
        // Markdig only accepts key=value pairs after the id and classes, so the width goes last.
        if (width is { } px)
            tokens.Add($"width={px}");
        var replacement = tokens.Count > 0 ? "{" + string.Join(' ', tokens) + "}" : "";
        var length = blockEnd > 0 ? blockEnd - end : 0;
        return replacement == text.Substring(end, length) ? null : (end, length, replacement);
    }

    private static List<string> SplitAttributes(string attributes)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        foreach (var ch in attributes)
        {
            if (quote != '\0')
            {
                current.Append(ch);
                if (ch == quote)
                    quote = '\0';
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                    tokens.Add(current.ToString());
                current.Clear();
            }
            else
            {
                if (ch is '"' or '\'')
                    quote = ch;
                current.Append(ch);
            }
        }
        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }

    private void Select(int start, int length)
    {
        editor.Select(start, length);
        editor.TextArea.Caret.BringCaretToView();
        editor.Focus();
    }

    // ------------------------------------------------------------ inline wraps

    /// <summary>
    /// Toggles a pair of inline markers around the selection (or the word at the caret). Mirrors Word: with no
    /// selection and no word, a placeholder is inserted and selected so typing replaces it.
    /// </summary>
    public void ToggleWrap(string prefix, string suffix, string placeholder)
    {
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        if (length == 0)
        {
            var (ws, we) = WordAt(editor.CaretOffset);
            if (we > ws)
            {
                start = ws;
                length = we - ws;
            }
        }
        else
        {
            // Keep whitespace outside the markers ("**word** " rather than "**word **").
            var raw = Doc.GetText(start, length);
            var lead = raw.Length - raw.TrimStart().Length;
            var trail = raw.Length - raw.TrimEnd().Length;
            if (lead + trail < raw.Length)
            {
                start += lead;
                length -= lead + trail;
            }
        }

        var text = Doc.GetText(start, length);
        using (Doc.RunUpdate())
        {
            if (text.Length >= prefix.Length + suffix.Length + 1 && text.StartsWith(prefix, StringComparison.Ordinal) && text.EndsWith(suffix, StringComparison.Ordinal) && !IsLongerMarker(text, prefix))
            {
                var inner = text[prefix.Length..^suffix.Length];
                Doc.Replace(start, length, inner);
                Select(start, inner.Length);
                return;
            }
            if (SurroundedBy(start, length, prefix, suffix))
            {
                Doc.Remove(start + length, suffix.Length);
                Doc.Remove(start - prefix.Length, prefix.Length);
                Select(start - prefix.Length, length);
                return;
            }
            if (length == 0)
            {
                Doc.Insert(start, prefix + placeholder + suffix);
                Select(start + prefix.Length, placeholder.Length);
            }
            else
            {
                Doc.Replace(start, length, prefix + text + suffix);
                Select(start + prefix.Length, length);
            }
        }
    }

    private static bool IsLongerMarker(string text, string prefix) =>
        prefix.Length == 1 && text.Length > 2 && text[1] == prefix[0] && !(text.Length > 3 && text[2] == prefix[0]);

    private bool SurroundedBy(int start, int length, string prefix, string suffix)
    {
        if (start < prefix.Length || start + length + suffix.Length > Doc.TextLength)
            return false;
        if (Doc.GetText(start - prefix.Length, prefix.Length) != prefix || Doc.GetText(start + length, suffix.Length) != suffix)
            return false;
        // "*" must not match the inner half of "**bold**" (but "***both***" is fine).
        if (prefix.Length == 1)
        {
            var before = start - 2 >= 0 ? Doc.GetCharAt(start - 2) : '\0';
            var after = start + length + 1 < Doc.TextLength ? Doc.GetCharAt(start + length + 1) : '\0';
            var beforeBefore = start - 3 >= 0 ? Doc.GetCharAt(start - 3) : '\0';
            if (before == prefix[0] && beforeBefore != prefix[0] && after == suffix[0])
                return false;
        }
        return true;
    }

    private (int Start, int End) WordAt(int offset)
    {
        var start = offset;
        var end = offset;
        while (start > 0 && IsWordChar(Doc.GetCharAt(start - 1)))
            start--;
        while (end < Doc.TextLength && IsWordChar(Doc.GetCharAt(end)))
            end++;
        return (start, end);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '\'' or '-';

    public void ClearFormatting()
    {
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        if (length == 0)
        {
            var line = Doc.GetLineByOffset(editor.CaretOffset);
            start = line.Offset;
            length = line.Length;
        }
        var text = Doc.GetText(start, length);
        var cleaned = InlineMarkerRegex().Replace(text, "${inner}");
        cleaned = HeadingRegex().Replace(cleaned, "");
        if (cleaned != text)
        {
            Doc.Replace(start, length, cleaned);
            Select(start, cleaned.Length);
        }
    }

    [GeneratedRegex(@"(\*\*\*|\*\*|\*|___|__|~~|==|\+\+|\^|~|`)(?<inner>[^\s].*?)\1", RegexOptions.Singleline)]
    private static partial Regex InlineMarkerRegex();

    // ------------------------------------------------------------- line formats

    private List<DocumentLine> SelectedLines()
    {
        var first = Doc.GetLineByOffset(editor.SelectionStart);
        var endOffset = editor.SelectionStart + editor.SelectionLength;
        var last = Doc.GetLineByOffset(endOffset);
        if (editor.SelectionLength > 0 && last.Offset == endOffset && last.LineNumber > first.LineNumber)
            last = last.PreviousLine;
        var lines = new List<DocumentLine>();
        for (var line = first; line is not null; line = line.NextLine)
        {
            lines.Add(line);
            if (line == last)
                break;
        }
        return lines;
    }

    private void ReplaceLines(List<DocumentLine> lines, Func<string, int, string> transform)
    {
        var start = lines[0].Offset;
        var end = lines[^1].EndOffset;
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                sb.Append(Doc.GetText(lines[i - 1].EndOffset, lines[i - 1].DelimiterLength));
            sb.Append(transform(Doc.GetText(lines[i]), i));
        }
        var replacement = sb.ToString();
        Doc.Replace(start, end - start, replacement);
        if (lines.Count == 1)
            Select(start + replacement.Length, 0);
        else
            Select(start, replacement.Length);
    }

    /// <summary>Sets the heading level of the selected lines (0 = normal paragraph). Re-applying a level removes it.</summary>
    public void SetHeading(int level)
    {
        var lines = SelectedLines();
        var allAtLevel = level > 0 && lines.All(l => HeadingRegex().Match(Doc.GetText(l)) is { Success: true } m && m.Groups["hashes"].Length == level);
        ReplaceLines(lines, (text, _) =>
        {
            var stripped = HeadingRegex().Replace(text, "", 1);
            if (allAtLevel || level == 0)
                return stripped;
            stripped = ListMarkerRegex().Replace(stripped, "", 1);
            stripped = QuoteRegex().Replace(stripped, "", 1);
            return new string('#', level) + " " + stripped.TrimStart();
        });
    }

    public int CurrentHeadingLevel()
    {
        var line = Doc.GetLineByOffset(editor.CaretOffset);
        var m = HeadingRegex().Match(Doc.GetText(line));
        return m.Success ? m.Groups["hashes"].Length : 0;
    }

    public void ToggleLineFormat(LineFormat format)
    {
        var lines = SelectedLines();
        var nonEmpty = lines.Where(l => l.Length > 0).ToList();
        if (nonEmpty.Count == 0)
            nonEmpty = lines;

        bool Has(string text) => format switch
        {
            LineFormat.Quote => QuoteRegex().IsMatch(text),
            LineFormat.Task => ListMarkerRegex().Match(text) is { Success: true } m && m.Groups["task"].Success,
            LineFormat.Numbered => ListMarkerRegex().Match(text) is { Success: true } m && char.IsDigit(m.Groups["marker"].Value[0]) && !m.Groups["task"].Success,
            _ => ListMarkerRegex().Match(text) is { Success: true } m && !char.IsDigit(m.Groups["marker"].Value[0]) && !m.Groups["task"].Success,
        };

        var remove = nonEmpty.All(l => Has(Doc.GetText(l)));
        var number = 1;
        ReplaceLines(lines, (text, _) =>
        {
            if (text.Length == 0 && lines.Count > 1)
                return text;
            if (format == LineFormat.Quote)
                return remove ? QuoteRegex().Replace(text, "${indent}", 1) : "> " + text;

            var m = ListMarkerRegex().Match(text);
            var indent = m.Success ? m.Groups["indent"].Value : text[..(text.Length - text.TrimStart().Length)];
            var body = m.Success ? text[m.Length..] : text.TrimStart();
            body = HeadingRegex().Replace(body, "", 1);
            if (remove)
                return indent + body;
            var marker = format switch
            {
                LineFormat.Numbered => $"{number++}. ",
                LineFormat.Task => "- [ ] ",
                _ => "- ",
            };
            return indent + marker + body;
        });
    }

    public void Indent(bool outdent)
    {
        var lines = SelectedLines();
        ReplaceLines(lines, (text, _) =>
        {
            if (!outdent)
                return text.Length == 0 ? text : "    " + text;
            if (text.StartsWith('\t'))
                return text[1..];
            var spaces = 0;
            while (spaces < 4 && spaces < text.Length && text[spaces] == ' ')
                spaces++;
            return text[spaces..];
        });
    }

    /// <summary>Handles Enter inside a list or quote: continues the list, or ends it on an empty item.</summary>
    public bool HandleEnter()
    {
        if (editor.SelectionLength > 0)
            return false;
        var line = Doc.GetLineByOffset(editor.CaretOffset);
        var text = Doc.GetText(line);
        var caretColumn = editor.CaretOffset - line.Offset;

        var m = ListMarkerRegex().Match(text);
        if (m.Success && caretColumn >= m.Length)
        {
            var body = text[m.Length..];
            if (body.Trim().Length == 0)
            {
                // Empty item: end the list.
                Doc.Replace(line.Offset, line.Length, "");
                editor.CaretOffset = line.Offset;
                return true;
            }
            var marker = m.Groups["marker"].Value;
            if (char.IsDigit(marker[0]))
            {
                var n = int.Parse(marker[..^1], CultureInfo.InvariantCulture);
                marker = (n + 1).ToString(CultureInfo.InvariantCulture) + marker[^1];
            }
            var task = m.Groups["task"].Success ? "[ ] " : "";
            var continuation = "\n" + m.Groups["indent"].Value + marker + m.Groups["space"].Value + task;
            Doc.Insert(editor.CaretOffset, continuation);
            return true;
        }

        var q = QuoteRegex().Match(text);
        if (q.Success && caretColumn >= q.Length)
        {
            if (text[q.Length..].Trim().Length == 0)
            {
                Doc.Replace(line.Offset, line.Length, "");
                editor.CaretOffset = line.Offset;
                return true;
            }
            Doc.Insert(editor.CaretOffset, "\n" + q.Groups["indent"].Value + "> ");
            return true;
        }
        return false;
    }

    public bool IsInList()
    {
        var line = Doc.GetLineByOffset(editor.CaretOffset);
        return ListMarkerRegex().IsMatch(Doc.GetText(line));
    }

    // ---------------------------------------------------------------- inserts

    /// <summary>Inserts block-level text on its own paragraph, separated by blank lines.</summary>
    private void InsertBlock(string block, int selectOffset, int selectLength)
    {
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        var lineStart = Doc.GetLineByOffset(start);
        var before = Doc.GetText(lineStart.Offset, start - lineStart.Offset);
        var endLine = Doc.GetLineByOffset(start + length);
        var after = Doc.GetText(start + length, endLine.EndOffset - (start + length));

        var prefix = "";
        if (before.Trim().Length > 0)
            prefix = "\n\n";
        else if (lineStart.PreviousLine is { } prev && Doc.GetText(prev).Trim().Length > 0)
            prefix = "\n";

        var suffix = "";
        if (after.Trim().Length > 0)
            suffix = "\n\n";
        else if (endLine.NextLine is { } next && Doc.GetText(next).Trim().Length > 0)
            suffix = "\n";
        else if (endLine.NextLine is null)
            suffix = "\n";

        using (Doc.RunUpdate())
        {
            Doc.Replace(start, length, prefix + block + suffix);
        }
        Select(start + prefix.Length + selectOffset, selectLength);
    }

    private string SelectedText => editor.SelectionLength > 0 ? Doc.GetText(editor.SelectionStart, editor.SelectionLength) : "";

    private void InsertInline(string text, int selectOffset, int selectLength)
    {
        var start = editor.SelectionStart;
        Doc.Replace(start, editor.SelectionLength, text);
        Select(start + selectOffset, selectLength);
    }

    public void InsertLink()
    {
        var selected = SelectedText.Trim();
        if (Uri.TryCreate(selected, UriKind.Absolute, out _) && selected.Contains(':'))
        {
            const string label = "link text";
            InsertInline($"[{label}]({selected})", 1, label.Length);
        }
        else if (selected.Length > 0)
        {
            const string url = "https://";
            InsertInline($"[{selected}]({url})", selected.Length + 3, url.Length);
        }
        else
        {
            const string label = "link text";
            InsertInline($"[{label}](https://)", 1, label.Length);
        }
    }

    public void InsertImage(string? path)
    {
        var alt = SelectedText.Trim();
        if (alt.Length == 0)
            alt = path is null ? "description" : Path.GetFileNameWithoutExtension(path);
        var target = path is null ? "image.png" : path.Replace('\\', '/').Replace(" ", "%20");
        InsertBlock($"![{alt}]({target})", 2, alt.Length);
    }

    public void InsertTable(int rows, int columns)
    {
        rows = Math.Max(1, rows);
        columns = Math.Max(1, columns);
        var widths = Enumerable.Range(1, columns).Select(c => $"Column {c}".Length).ToArray();
        var sb = new StringBuilder();
        sb.Append('|');
        for (var c = 0; c < columns; c++)
            sb.Append(' ').Append($"Column {c + 1}").Append(" |");
        sb.Append('\n').Append('|');
        for (var c = 0; c < columns; c++)
            sb.Append(' ').Append(new string('-', widths[c])).Append(" |");
        for (var r = 0; r < rows - 1; r++)
        {
            sb.Append('\n').Append('|');
            for (var c = 0; c < columns; c++)
                sb.Append(' ').Append(new string(' ', widths[c])).Append(" |");
        }
        InsertBlock(sb.ToString(), 2, "Column 1".Length);
    }

    public void InsertCodeBlock(string language)
    {
        var body = SelectedText;
        var block = $"```{language}\n{body}{(body.EndsWith('\n') || body.Length == 0 ? "" : "\n")}```";
        var caret = 4 + language.Length;
        InsertBlock(block, caret, body.TrimEnd('\n').Length);
    }

    public void InsertMathBlock()
    {
        var body = SelectedText.Trim();
        if (body.Length == 0)
            body = @"E = mc^2";
        InsertBlock($"$$\n{body}\n$$", 3, body.Length);
    }

    public void InsertInlineMath() => ToggleWrap("$", "$", @"x^2");

    public void InsertHorizontalRule() => InsertBlock("---", 3, 0);

    public void InsertAlert(string kind)
    {
        var body = SelectedText.Trim();
        if (body.Length == 0)
            body = "Text";
        var quoted = string.Join("\n", body.Split('\n').Select(l => "> " + l.TrimEnd('\r')));
        var header = $"> [!{kind}]\n";
        InsertBlock(header + quoted, header.Length + 2, body.Split('\n')[0].TrimEnd('\r').Length);
    }

    public void InsertDefinition()
    {
        var term = SelectedText.Trim();
        if (term.Length == 0)
            term = "Term";
        InsertBlock($"{term}\n:   Definition", 0, term.Length);
    }

    public void InsertFencedDiv(string cls)
    {
        var body = SelectedText.Trim();
        if (body.Length == 0)
            body = "Content";
        var header = $"::: {cls}\n";
        InsertBlock($"{header}{body}\n:::", header.Length, body.Length);
    }

    public void InsertFootnote()
    {
        var text = Doc.Text;
        var next = FootnoteRefRegex().Matches(text).Select(m => int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() + 1;
        var reference = $"[^{next}]";
        var insertAt = editor.SelectionStart + editor.SelectionLength;
        const string placeholder = "Footnote text.";

        using (Doc.RunUpdate())
        {
            Doc.Insert(insertAt, reference);
            var end = Doc.TextLength;
            var trailing = Doc.Text.EndsWith("\n\n") ? "" : Doc.Text.EndsWith('\n') ? "\n" : "\n\n";
            Doc.Insert(end, $"{trailing}[^{next}]: {placeholder}\n");
        }
        var noteOffset = Doc.TextLength - placeholder.Length - 1;
        Select(noteOffset, placeholder.Length);
        editor.ScrollToLine(Doc.GetLineByOffset(noteOffset).LineNumber);
    }

    public void InsertAbbreviation()
    {
        var abbr = SelectedText.Trim();
        if (abbr.Length == 0 || abbr.Contains('\n'))
            abbr = "HTML";
        const string placeholder = "Full description";
        using (Doc.RunUpdate())
        {
            var trailing = Doc.Text.EndsWith("\n\n") ? "" : Doc.Text.EndsWith('\n') ? "\n" : "\n\n";
            Doc.Insert(Doc.TextLength, $"{trailing}*[{abbr}]: {placeholder}\n");
        }
        var offset = Doc.TextLength - placeholder.Length - 1;
        Select(offset, placeholder.Length);
        editor.ScrollToLine(Doc.GetLineByOffset(offset).LineNumber);
    }

    public void InsertText(string text) => InsertInline(text, text.Length, 0);

    public void SetStyle(string style)
    {
        switch (style)
        {
            case "normal":
                SetHeading(0);
                var lines = SelectedLines();
                ReplaceLines(lines, (t, _) => QuoteRegex().Replace(ListMarkerRegex().Replace(t, "${indent}", 1), "${indent}", 1));
                break;
            case "quote":
                ToggleLineFormat(LineFormat.Quote);
                break;
            case "code":
                InsertCodeBlock("");
                break;
            default:
                if (style.StartsWith('h') && int.TryParse(style[1..], out var level))
                    SetHeading(level);
                break;
        }
    }
}
