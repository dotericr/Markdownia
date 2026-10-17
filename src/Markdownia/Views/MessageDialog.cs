using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Markdownia.Views;

public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Small modal dialogs built in code (no reflection-based view location, keeps AOT simple).</summary>
public static class MessageDialog
{
    public static async Task<SaveChoice> AskSaveAsync(Window owner, string documentName)
    {
        var result = SaveChoice.Cancel;
        var dialog = CreateDialog("Markdownia",
            $"Do you want to save the changes to “{documentName}”?",
            "Your changes will be lost if you don't save them.");

        var save = new Button { Content = "Save", Classes = { "accent" }, IsDefault = true, MinWidth = 90 };
        var discard = new Button { Content = "Don't Save", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        save.Click += (_, _) => { result = SaveChoice.Save; dialog.Close(); };
        discard.Click += (_, _) => { result = SaveChoice.Discard; dialog.Close(); };
        cancel.Click += (_, _) => { result = SaveChoice.Cancel; dialog.Close(); };
        AddButtons(dialog, save, discard, cancel);

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task ShowAsync(Window owner, string title, string message)
    {
        var dialog = CreateDialog("Markdownia", title, message);
        var ok = new Button { Content = "OK", Classes = { "accent" }, IsDefault = true, IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => dialog.Close();
        AddButtons(dialog, ok);
        await dialog.ShowDialog(owner);
    }

    private static Window CreateDialog(string title, string heading, string body)
    {
        var content = new StackPanel { Spacing = 8, Margin = new Thickness(24, 22, 24, 18) };
        content.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = body, FontSize = 13, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        return new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Children = { content } },
            [!Window.BackgroundProperty] = Application.Current!.GetResourceObservable("Md.ChromeBrush").ToBinding(),
        };
    }

    private static void AddButtons(Window dialog, params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 0, 24, 20),
        };
        foreach (var b in buttons)
            row.Children.Add(b);
        var dock = (DockPanel)dialog.Content!;
        DockPanel.SetDock(row, Dock.Bottom);
        dock.Children.Insert(0, row);
    }
}
