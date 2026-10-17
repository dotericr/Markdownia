using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using Markdownia.Editing;

namespace Markdownia.Tests;

public class EditingTests
{
    private static (TextEditor Editor, MarkdownEditing Editing) Create(string text, int selectionStart = 0, int selectionLength = 0)
    {
        var editor = new TextEditor { Text = text };
        editor.Select(selectionStart, selectionLength);
        return (editor, new MarkdownEditing(editor));
    }

    [AvaloniaFact]
    public void Bold_wraps_the_word_at_the_caret_and_toggles_off()
    {
        var (editor, editing) = Create("hello world");
        editor.CaretOffset = 8;
        editing.ToggleWrap("**", "**", "bold");
        Assert.Equal("hello **world**", editor.Text);

        editing.ToggleWrap("**", "**", "bold");
        Assert.Equal("hello world", editor.Text);
    }

    [AvaloniaFact]
    public void Wrap_keeps_surrounding_whitespace_outside_markers()
    {
        var (editor, editing) = Create("say hello there", 4, 6);
        editing.ToggleWrap("*", "*", "x");
        Assert.Equal("say *hello* there", editor.Text);
    }

    [AvaloniaFact]
    public void Italic_does_not_unwrap_bold()
    {
        var (editor, editing) = Create("**word**", 2, 4);
        editing.ToggleWrap("*", "*", "x");
        Assert.Equal("***word***", editor.Text);
    }

    [AvaloniaFact]
    public void Wrap_with_no_word_inserts_selected_placeholder()
    {
        var (editor, editing) = Create("a  b", 2, 0);
        editing.ToggleWrap("`", "`", "code");
        Assert.Equal("a `code` b", editor.Text);
        Assert.Equal("code", editor.SelectedText);
    }

    [AvaloniaFact]
    public void Heading_is_applied_replaced_and_toggled()
    {
        var (editor, editing) = Create("Title");
        editing.SetHeading(2);
        Assert.Equal("## Title", editor.Text);
        editing.SetHeading(1);
        Assert.Equal("# Title", editor.Text);
        editing.SetHeading(1);
        Assert.Equal("Title", editor.Text);
    }

    [AvaloniaFact]
    public void Numbered_list_numbers_each_selected_line()
    {
        var (editor, editing) = Create("one\ntwo\nthree", 0, 13);
        editing.ToggleLineFormat(LineFormat.Numbered);
        Assert.Equal("1. one\n2. two\n3. three", editor.Text);

        editor.Select(0, editor.Text.Length);
        editing.ToggleLineFormat(LineFormat.Bullet);
        Assert.Equal("- one\n- two\n- three", editor.Text);

        editor.Select(0, editor.Text.Length);
        editing.ToggleLineFormat(LineFormat.Bullet);
        Assert.Equal("one\ntwo\nthree", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_continues_and_ends_lists()
    {
        var (editor, editing) = Create("1. first");
        editor.CaretOffset = editor.Text.Length;
        Assert.True(editing.HandleEnter());
        Assert.Equal("1. first\n2. ", editor.Text);

        Assert.True(editing.HandleEnter());
        Assert.Equal("1. first\n", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_continues_task_lists_unchecked()
    {
        var (editor, editing) = Create("- [x] done");
        editor.CaretOffset = editor.Text.Length;
        Assert.True(editing.HandleEnter());
        Assert.Equal("- [x] done\n- [ ] ", editor.Text);
    }

    [AvaloniaFact]
    public void Table_is_inserted_as_its_own_block()
    {
        var (editor, editing) = Create("Intro");
        editor.CaretOffset = 5;
        editing.InsertTable(2, 2);
        Assert.Equal("Intro\n\n| Column 1 | Column 2 |\n| -------- | -------- |\n|          |          |\n", editor.Text);
    }

    [AvaloniaFact]
    public void Footnotes_are_numbered_after_existing_ones()
    {
        var (editor, editing) = Create("Text[^1].\n\n[^1]: First.\n");
        editor.CaretOffset = 4;
        editing.InsertFootnote();
        Assert.Contains("Text[^2][^1].", editor.Text);
        Assert.EndsWith("[^2]: Footnote text.\n", editor.Text);
        Assert.Equal("Footnote text.", editor.SelectedText);
    }

    [AvaloniaFact]
    public void Indent_and_outdent_shift_by_four_spaces()
    {
        var (editor, editing) = Create("- item");
        editing.Indent(outdent: false);
        Assert.Equal("    - item", editor.Text);
        editing.Indent(outdent: true);
        Assert.Equal("- item", editor.Text);
    }
}
