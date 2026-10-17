using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdownia.Controls;
using Markdownia.Editing;
using Markdownia.Rendering;
using Markdownia.Services;
using Markdownia.Theming;

namespace Markdownia.Views;

public partial class MainWindow : Window
{
    private static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".txt", ".text"];

    private static readonly (string Label, double Value)[] LineSpacings =
    [
        ("Compact · 1.35", 1.35),
        ("Snug · 1.5", 1.5),
        ("Comfortable · 1.6", 1.6),
        ("Relaxed · 1.75", 1.75),
        ("Airy · 1.9", 1.9),
    ];

    private static readonly (string Label, double Em)[] ContentWidths =
    [
        ("Narrow", 30),
        ("Medium", 36),
        ("Wide", 46),
        ("Full", 0),
    ];

    private static readonly string[] RenderingModes = ["Smooth", "Sharp", "Subpixel"];

    private readonly AppSettings _settings;
    private readonly MarkdownEditing _editing;
    private readonly MarkdownHighlighting _highlighting;
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _statsTimer;

    private ParsedDocument? _lastParsed;
    private string? _filePath;
    private bool _isGuide;
    private int _renderVersion;
    private ScrollLeader _scrollLeader = ScrollLeader.Editor;
    private ScrollViewer? _editorScroll;

    private enum ScrollLeader { Editor, Preview }
    private bool _closeConfirmed;
    private bool _updatingUi;

    public MainWindow() : this(new AppSettings())
    {
    }

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Icon = AppIcon.Create();
        DefaultAppItem.IsVisible = DesktopIntegration.CanSetDefault;
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);
        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;

        _editing = new MarkdownEditing(Editor);
        _highlighting = new MarkdownHighlighting(ThemeManager.Current);

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            _ = RenderAsync(reparse: true);
        };
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _statsTimer.Tick += (_, _) =>
        {
            _statsTimer.Stop();
            UpdateWordCount();
        };

        SetupEditor();
        SetupRibbon();
        SetupPreview();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        PaneSplitter.DragCompleted += (_, _) => RememberPaneRatio();

        ThemeManager.ThemeChanged += (_, _) => OnThemeChanged();
        ApplyEditorSettings();
        ApplyEditorTheme();
        ApplyLayout();
        UpdateRibbonState();
        ApplyRibbonCollapsed();

        if (settings.RecentFiles.Count == 0)
            LoadDocument(LoadGuide(), null, isGuide: true);
        else
            LoadDocument("", null);
    }

    public void OpenFileOnStartup(string path) => Dispatcher.UIThread.Post(() => OpenPath(path, confirm: false));

    // ================================================================ setup

    private void SetupEditor()
    {
        Editor.SyntaxHighlighting = _highlighting;
        var options = Editor.Options;
        options.HighlightCurrentLine = true;
        options.ConvertTabsToSpaces = true;
        options.IndentationSize = 4;
        options.EnableHyperlinks = false;
        options.EnableEmailHyperlinks = false;
        options.AllowScrollBelowDocument = true;
        options.EnableRectangularSelection = true;
        options.CutCopyWholeLine = true;

        Editor.TextChanged += (_, _) =>
        {
            UpdateTitle();
            _renderTimer.Stop();
            _renderTimer.Start();
            _statsTimer.Stop();
            _statsTimer.Start();
        };
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretStatus();
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncPreviewToEditor();
        Preview.ScrollViewer.ScrollChanged += (_, e) =>
        {
            if (e.OffsetDelta.Y != 0)
                SyncEditorToPreview();
        };
        // Scroll sync follows whichever pane the user is working in, so the panes never drive each other in a loop.
        void Lead(Control pane, ScrollLeader leader)
        {
            pane.AddHandler(PointerEnteredEvent, (_, _) => _scrollLeader = leader, handledEventsToo: true);
            pane.AddHandler(PointerPressedEvent, (_, _) => _scrollLeader = leader, RoutingStrategies.Tunnel, handledEventsToo: true);
            pane.AddHandler(PointerWheelChangedEvent, (_, _) => _scrollLeader = leader, RoutingStrategies.Tunnel, handledEventsToo: true);
            pane.AddHandler(KeyDownEvent, (_, _) => _scrollLeader = leader, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        Lead(EditorHost, ScrollLeader.Editor);
        Lead(Preview, ScrollLeader.Preview);
        Editor.TextArea.Caret.PositionChanged += (_, _) => _scrollLeader = ScrollLeader.Editor;
        Editor.TextArea.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
    }

    private void SetupPreview()
    {
        Preview.ZoomRequested += delta => ChangePreviewSize(delta);
    }

    private void SetupRibbon()
    {
        _updatingUi = true;

        // Combos
        // Show each font name in its own typeface, like Word's font picker.
        PreviewFontCombo.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            new TextBlock { Text = name, FontFamily = FontCatalog.ResolveReading(name), FontSize = 13 });
        PreviewFontCombo.ItemsSource = FontCatalog.ReadingFontNames();
        PreviewFontCombo.SelectedItem = PreviewFontCombo.ItemsSource.Cast<string>().Contains(_settings.PreviewFont) ? _settings.PreviewFont : FontCatalog.ReadingFonts[0].Name;
        PreviewFontCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || PreviewFontCombo.SelectedItem is not string name)
                return;
            _settings.PreviewFont = name;
            RefreshStyleGallery();
            _ = RenderAsync(reparse: false);
        };

        CodeFontCombo.ItemsSource = FontCatalog.CodeFontNames();
        CodeFontCombo.SelectedItem = CodeFontCombo.ItemsSource.Cast<string>().Contains(_settings.CodeFont) ? _settings.CodeFont : FontCatalog.CodeFonts[0].Name;
        CodeFontCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || CodeFontCombo.SelectedItem is not string name)
                return;
            _settings.CodeFont = name;
            _ = RenderAsync(reparse: false);
        };

        EditorFontCombo.ItemsSource = FontCatalog.CodeFontNames();
        EditorFontCombo.SelectedItem = EditorFontCombo.ItemsSource.Cast<string>().Contains(_settings.EditorFont) ? _settings.EditorFont : FontCatalog.CodeFonts[0].Name;
        EditorFontCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || EditorFontCombo.SelectedItem is not string name)
                return;
            _settings.EditorFont = name;
            ApplyEditorSettings();
        };

        LineSpacingCombo.ItemsSource = LineSpacings.Select(l => l.Label).ToList();
        LineSpacingCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || LineSpacingCombo.SelectedIndex < 0)
                return;
            _settings.LineSpacing = LineSpacings[LineSpacingCombo.SelectedIndex].Value;
            _ = RenderAsync(reparse: false);
        };

        WidthCombo.ItemsSource = ContentWidths.Select(w => w.Label).ToList();
        WidthCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || WidthCombo.SelectedIndex < 0)
                return;
            _settings.ContentWidth = ContentWidths[WidthCombo.SelectedIndex].Label;
            ApplyPreviewMeasure();
        };

        RenderingCombo.ItemsSource = RenderingModes;
        RenderingCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || RenderingCombo.SelectedItem is not string mode)
                return;
            _settings.TextRendering = mode;
            Preview.SetTextRendering(mode);
        };

        // Toggles
        JustifyToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
                return;
            _settings.Justify = JustifyToggle.IsChecked == true;
            _ = RenderAsync(reparse: false);
        };
        SmartToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
                return;
            _settings.SmartTypography = SmartToggle.IsChecked == true;
            _ = RenderAsync(reparse: true);
        };
        WordWrapToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
                return;
            _settings.WordWrap = WordWrapToggle.IsChecked == true;
            ApplyEditorSettings();
        };
        LineNumbersToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
                return;
            _settings.ShowLineNumbers = LineNumbersToggle.IsChecked == true;
            ApplyEditorSettings();
        };
        SyncScrollToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updatingUi)
                return;
            _settings.SyncScroll = SyncScrollToggle.IsChecked == true;
            SyncPreviewToEditor();
        };

        ZoomSlider.ValueChanged += (_, e) =>
        {
            if (_updatingUi)
                return;
            SetPreviewSize(e.NewValue);
        };

        BuildStyleGallery();
        BuildThemeGallery();
        BuildTablePicker();
        BuildCodeBlockMenu();
        BuildSymbolPicker();
        HeadingLinkButton.Flyout = BuildHeadingFlyout(insertLink: true);
        OutlineButton.Flyout = BuildHeadingFlyout(insertLink: false);

        if (FileButton.Flyout is MenuFlyout fileMenu)
            fileMenu.Opening += (_, _) => RebuildRecentMenu();

        _updatingUi = false;
    }

    /// <summary>Pushes the current settings into every ribbon control without triggering their handlers.</summary>
    private void UpdateRibbonState()
    {
        _updatingUi = true;
        PreviewSizeText.Text = _settings.PreviewFontSize.ToString("0", CultureInfo.InvariantCulture);
        EditorSizeText.Text = _settings.EditorFontSize.ToString("0", CultureInfo.InvariantCulture);
        ZoomSlider.Value = _settings.PreviewFontSize;
        ZoomText.Text = $"{_settings.PreviewFontSize:0} px";

        var spacingIndex = Array.FindIndex(LineSpacings, l => Math.Abs(l.Value - _settings.LineSpacing) < 0.01);
        LineSpacingCombo.SelectedIndex = spacingIndex >= 0 ? spacingIndex : 2;
        var widthIndex = Array.FindIndex(ContentWidths, w => w.Label == _settings.ContentWidth);
        WidthCombo.SelectedIndex = widthIndex >= 0 ? widthIndex : 1;
        RenderingCombo.SelectedItem = RenderingModes.Contains(_settings.TextRendering) ? _settings.TextRendering : "Smooth";

        JustifyToggle.IsChecked = _settings.Justify;
        SmartToggle.IsChecked = _settings.SmartTypography;
        WordWrapToggle.IsChecked = _settings.WordWrap;
        LineNumbersToggle.IsChecked = _settings.ShowLineNumbers;
        SyncScrollToggle.IsChecked = _settings.SyncScroll;

        var mode = LayoutMode;
        LayoutEditorToggle.IsChecked = StatusEditorToggle.IsChecked = mode == "editor";
        LayoutSplitToggle.IsChecked = StatusSplitToggle.IsChecked = mode == "split";
        LayoutReaderToggle.IsChecked = StatusReaderToggle.IsChecked = mode == "reader";
        _updatingUi = false;
    }

    // ============================================================ galleries

    private void BuildStyleGallery()
    {
        var styles = new (string Tag, string Label, double Size, FontWeight Weight, bool Mono, bool Quote)[]
        {
            ("normal", "Normal", 13, FontWeight.Normal, false, false),
            ("h1", "Heading 1", 18, FontWeight.SemiBold, false, false),
            ("h2", "Heading 2", 15.5, FontWeight.SemiBold, false, false),
            ("h3", "Heading 3", 13.5, FontWeight.SemiBold, false, false),
            ("h4", "Heading 4", 12.5, FontWeight.SemiBold, false, false),
            ("quote", "Quote", 13, FontWeight.Normal, false, true),
            ("code", "Code", 12, FontWeight.Normal, true, false),
        };
        StyleGallery.Children.Clear();
        foreach (var style in styles)
        {
            var sample = new TextBlock
            {
                Text = style.Mono ? "{ x }" : "AaBbCc",
                FontSize = style.Size,
                FontWeight = style.Weight,
                FontStyle = style.Quote ? FontStyle.Italic : FontStyle.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = style.Mono ? "mono" : "body",
            };
            Control sampleHost = sample;
            if (style.Quote)
            {
                sampleHost = new Border
                {
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Padding = new Thickness(5, 0, 0, 0),
                    Child = sample,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    [!Border.BorderBrushProperty] = this.GetResourceObservable("Md.ChromeMutedBrush").ToBinding(),
                };
            }
            var tile = new Border
            {
                Width = 70,
                Height = 38,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                Child = sampleHost,
                ClipToBounds = true,
                [!Border.BackgroundProperty] = this.GetResourceObservable("Md.PreviewBackgroundBrush").ToBinding(),
                [!Border.BorderBrushProperty] = this.GetResourceObservable("Md.ChromeBorderBrush").ToBinding(),
            };
            sample[!TextBlock.ForegroundProperty] = this.GetResourceObservable("Md.PreviewForegroundBrush").ToBinding();
            var button = new Button
            {
                Classes = { "gallery" },
                Tag = style.Tag,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        tile,
                        new TextBlock
                        {
                            Text = style.Label,
                            FontSize = 11,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            [!TextBlock.ForegroundProperty] = this.GetResourceObservable("Md.ChromeForegroundBrush").ToBinding(),
                        },
                    },
                },
            };
            ToolTip.SetTip(button, style.Tag switch
            {
                "normal" => "Normal text (Ctrl+0)",
                "quote" => "Block quote",
                "code" => "Code block",
                _ => $"{style.Label} (Ctrl+{style.Tag[1..]})",
            });
            button.Click += (_, _) => RunEdit(() => _editing.SetStyle(style.Tag));
            StyleGallery.Children.Add(button);
        }
        RefreshStyleGallery();
    }

    private void RefreshStyleGallery()
    {
        var body = FontCatalog.ResolveReading(_settings.PreviewFont);
        var mono = FontCatalog.ResolveCode(_settings.CodeFont);
        foreach (var text in StyleGallery.GetVisualDescendants().OfType<TextBlock>())
        {
            if (text.Tag as string == "body")
                text.FontFamily = body;
            else if (text.Tag as string == "mono")
                text.FontFamily = mono;
        }
    }

    private void BuildThemeGallery()
    {
        ThemeGallery.Children.Clear();
        foreach (var theme in Themes.All)
        {
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var color in new[] { theme.Accent, theme.Syntax.Keyword, theme.Syntax.String, theme.Syntax.Type })
                dots.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color) });

            var swatch = new Border
            {
                Width = 62,
                Height = 38,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(theme.Background),
                BorderBrush = new SolidColorBrush(ThemeManager.Blend(theme.Foreground, theme.Background, 0.18)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Aa",
                            FontFamily = FontCatalog.ReadingFonts[0].Family,
                            FontSize = 15,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(theme.Heading),
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        dots,
                    },
                },
            };
            var button = new Button
            {
                Classes = { "gallery" },
                Tag = theme.Id,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        swatch,
                        new TextBlock
                        {
                            Text = theme.Name,
                            FontSize = 10.5,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            MaxWidth = 66,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            [!TextBlock.ForegroundProperty] = this.GetResourceObservable("Md.ChromeForegroundBrush").ToBinding(),
                        },
                    },
                },
            };
            ToolTip.SetTip(button, $"{theme.Name} ({(theme.IsDark ? "dark" : "light")})");
            button.Click += (_, _) =>
            {
                _settings.ThemeId = theme.Id;
                ThemeManager.Apply(theme);
            };
            ThemeGallery.Children.Add(button);
        }
        RefreshThemeGallery();
    }

    private void RefreshThemeGallery()
    {
        foreach (var button in ThemeGallery.Children.OfType<Button>())
            button.Classes.Set("selected", button.Tag as string == ThemeManager.Current.Id);
    }

    private void BuildTablePicker()
    {
        const int rows = 8, cols = 10;
        var cells = new Border[rows, cols];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var cell = new Border
                {
                    Width = 17,
                    Height = 17,
                    Margin = new Thickness(1.5),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.Transparent,
                    [!Border.BorderBrushProperty] = this.GetResourceObservable("Md.ChromeMutedBrush").ToBinding(),
                };
                var (row, col) = (r, c);
                cell.PointerEntered += (_, _) =>
                {
                    for (var i = 0; i < rows; i++)
                    {
                        for (var j = 0; j < cols; j++)
                        {
                            var on = i <= row && j <= col;
                            if (on)
                                cells[i, j][!Border.BackgroundProperty] = this.GetResourceObservable("Md.ChromeCheckedBrush").ToBinding();
                            else
                            {
                                cells[i, j].ClearValue(Border.BackgroundProperty);
                                cells[i, j].Background = Brushes.Transparent;
                            }
                        }
                    }
                    TablePickerLabel.Text = $"{col + 1} × {row + 1} Table";
                };
                cell.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    TableButton.Flyout?.Hide();
                    RunEdit(() => _editing.InsertTable(row + 1, col + 1));
                };
                cells[r, c] = cell;
                TablePicker.Children.Add(cell);
            }
        }
    }

    private void BuildCodeBlockMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var (label, fence) in Languages.Common)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => RunEdit(() => _editing.InsertCodeBlock(fence));
            menu.Items.Add(item);
        }
        CodeBlockButton.Flyout = menu;
    }

    private void BuildSymbolPicker()
    {
        const string symbols = "—–…“”‘’«»©®™§¶†‡•·°±×÷≈≠≤≥∞√∑∏∫∂∆∇πΩαβγδλμσθ→←↑↓↔⇒⇐⇔✓✗★☆€£¥¢½¼¾‰№";
        var panel = new WrapPanel { MaxWidth = 300 };
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = panel };
        foreach (var rune in symbols.EnumerateRunes())
        {
            var text = rune.ToString();
            var button = new Button
            {
                Content = text,
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(1),
                FontSize = 15,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Focusable = false,
            };
            button.Click += (_, _) =>
            {
                flyout.Hide();
                RunEdit(() => _editing.InsertText(text));
            };
            panel.Children.Add(button);
        }
        SymbolButton.Flyout = flyout;
    }

    private MenuFlyout BuildHeadingFlyout(bool insertLink)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var headings = _lastParsed?.Document.Descendants<HeadingBlock>().ToList() ?? [];
            if (headings.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "No headings yet", IsEnabled = false });
                return;
            }
            foreach (var heading in headings.Take(80))
            {
                var text = HeadingText(heading);
                var id = heading.GetAttributes().Id ?? "";
                var item = new MenuItem
                {
                    Header = new TextBlock
                    {
                        Text = text,
                        Margin = new Thickness((heading.Level - 1) * 14, 0, 0, 0),
                        FontWeight = heading.Level <= 2 ? FontWeight.SemiBold : FontWeight.Normal,
                        MaxWidth = 420,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                };
                var line = heading.Line;
                item.Click += (_, _) =>
                {
                    if (insertLink)
                        RunEdit(() => _editing.InsertText($"[{text}](#{id})"));
                    else
                        GoToHeading(line, id);
                };
                menu.Items.Add(item);
            }
        };
        return menu;
    }

    private static string HeadingText(HeadingBlock heading)
    {
        if (heading.Inline is null)
            return "";
        var sb = new System.Text.StringBuilder();
        foreach (var inline in heading.Inline.Descendants<Markdig.Syntax.Inlines.LiteralInline>())
            sb.Append(inline.Content.ToString());
        foreach (var code in heading.Inline.Descendants<Markdig.Syntax.Inlines.CodeInline>())
            sb.Append(code.Content);
        return sb.ToString().Trim();
    }

    private void GoToHeading(int line, string id)
    {
        if (_settings.ShowEditor && line + 1 <= Editor.Document.LineCount)
        {
            var docLine = Editor.Document.GetLineByNumber(line + 1);
            Editor.CaretOffset = docLine.Offset;
            Editor.TextArea.TextView.EnsureVisualLines();
            var top = Editor.TextArea.TextView.GetVisualTopByDocumentLine(line + 1);
            ScrollEditorTo(top);
            Editor.Focus();
        }
        if (_settings.ShowPreview && id.Length > 0)
            Dispatcher.UIThread.Post(() => Preview.ScrollToAnchor(id), DispatcherPriority.Background);
    }

    private void RebuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        var recent = _settings.RecentFiles.Where(File.Exists).Take(12).ToList();
        if (recent.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "No recent documents", IsEnabled = false });
            return;
        }
        foreach (var path in recent)
        {
            var item = new MenuItem { Header = Path.GetFileName(path) };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => OpenPath(path, confirm: true);
            RecentMenu.Items.Add(item);
        }
    }

    // ============================================================ rendering

    private PreviewStyle CurrentPreviewStyle() => new()
    {
        Theme = ThemeManager.Current,
        BodyFont = FontCatalog.ResolveReading(_settings.PreviewFont),
        MonoFont = FontCatalog.ResolveCode(_settings.CodeFont),
        FontSize = _settings.PreviewFontSize,
        LineSpacing = _settings.LineSpacing,
        Justify = _settings.Justify,
        BaseDirectory = _filePath is null ? null : Path.GetDirectoryName(_filePath),
    };

    private async Task RenderAsync(bool reparse)
    {
        var version = ++_renderVersion;
        var parsed = _lastParsed;
        if (reparse || parsed is null)
        {
            var text = Editor.Text ?? "";
            var smart = _settings.SmartTypography;
            parsed = await Task.Run(() => MarkdownService.Parse(text, smart));
            if (version != _renderVersion)
                return;
            _lastParsed = parsed;
        }

        var renderer = new MarkdownRenderer(CurrentPreviewStyle(), OnLinkActivated, OnImageResized);
        RenderResult result;
        try
        {
            result = renderer.Render(parsed);
        }
        catch (Exception ex)
        {
            // A rendering bug must never take down the editor; show it in place of the preview.
            result = new RenderResult(new SelectableTextBlock { Text = "Preview error:\n" + ex, TextWrapping = TextWrapping.Wrap }, [], []);
        }
        ApplyPreviewMeasure();
        Preview.Show(result);
        if (_settings.SyncScroll && _settings.ShowEditor && _scrollLeader == ScrollLeader.Editor)
            Dispatcher.UIThread.Post(SyncPreviewToEditor, DispatcherPriority.Background);
    }

    private void ApplyPreviewMeasure()
    {
        var em = ContentWidths.FirstOrDefault(w => w.Label == _settings.ContentWidth, ContentWidths[1]).Em;
        Preview.SetMeasure(em, _settings.PreviewFontSize);
        Preview.SetTextRendering(_settings.TextRendering);
    }

    private void SyncPreviewToEditor()
    {
        if (!_settings.SyncScroll || !_settings.ShowEditor || !_settings.ShowPreview || _scrollLeader != ScrollLeader.Editor)
            return;
        var textView = Editor.TextArea.TextView;
        if (!textView.VisualLinesValid)
            return;
        var y = textView.ScrollOffset.Y;
        var line = textView.GetDocumentLineByVisualTop(y);
        if (line is null)
            return;
        var top = textView.GetVisualTopByDocumentLine(line.LineNumber);
        var next = line.NextLine is { } n ? textView.GetVisualTopByDocumentLine(n.LineNumber) : top + textView.DefaultLineHeight;
        var fraction = next > top ? Math.Clamp((y - top) / (next - top), 0, 1) : 0;
        var atEnd = Editor.VerticalOffset > 0 && Editor.VerticalOffset >= Editor.ExtentHeight - Editor.ViewportHeight - 2;
        Preview.SyncToSourceLine(line.LineNumber - 1 + fraction, atEnd);
    }

    private void SyncEditorToPreview()
    {
        if (!_settings.SyncScroll || !_settings.ShowEditor || !_settings.ShowPreview || _scrollLeader != ScrollLeader.Preview)
            return;
        if (Preview.IsScrolledToEnd)
        {
            ScrollEditorTo(Editor.ExtentHeight - Editor.ViewportHeight);
            return;
        }
        if (Preview.SourceLineAtTop() is not { } line)
            return;
        var doc = Editor.Document;
        var number = Math.Clamp((int)Math.Floor(line) + 1, 1, doc.LineCount);
        var textView = Editor.TextArea.TextView;
        var top = textView.GetVisualTopByDocumentLine(number);
        var next = number < doc.LineCount ? textView.GetVisualTopByDocumentLine(number + 1) : top + textView.DefaultLineHeight;
        var y = line <= 0.01 ? 0 : top + (line - Math.Floor(line)) * (next - top);
        ScrollEditorTo(y);
    }

    /// <summary>
    /// Scrolls the editor to a vertical offset. (<see cref="TextEditor.ScrollToVerticalOffset"/> has no effect in
    /// AvaloniaEdit 12, so this sets the offset on the editor's scroll viewer directly.)
    /// </summary>
    private void ScrollEditorTo(double y)
    {
        _editorScroll ??= Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_editorScroll is not null)
            _editorScroll.Offset = new Vector(_editorScroll.Offset.X, Math.Max(0, y));
    }

    private void OnThemeChanged()
    {
        ApplyEditorTheme();
        RefreshThemeGallery();
        _ = RenderAsync(reparse: false);
    }

    private void ApplyEditorTheme()
    {
        var t = ThemeManager.Current;
        Editor.TextArea.SelectionBrush = new SolidColorBrush(t.Selection);
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(t.EditorCurrentLine);
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        Editor.TextArea.Caret.CaretBrush = new SolidColorBrush(t.Accent);
        Editor.LineNumbersForeground = new SolidColorBrush(t.EditorLineNumbers);
        _highlighting.ApplyTheme(t);
        Editor.SyntaxHighlighting = null;
        Editor.SyntaxHighlighting = _highlighting;
    }

    private void ApplyEditorSettings()
    {
        Editor.FontFamily = FontCatalog.ResolveCode(_settings.EditorFont);
        Editor.FontSize = _settings.EditorFontSize;
        Editor.WordWrap = _settings.WordWrap;
        Editor.HorizontalScrollBarVisibility = _settings.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Editor.ShowLineNumbers = _settings.ShowLineNumbers;
        Editor.FontFeatures = [FontFeature.Parse("-calt"), FontFeature.Parse("-liga")];
        EditorSizeText.Text = _settings.EditorFontSize.ToString("0", CultureInfo.InvariantCulture);
    }

    // =============================================================== layout

    private string LayoutMode => (_settings.ShowEditor, _settings.ShowPreview) switch
    {
        (true, false) => "editor",
        (false, true) => "reader",
        _ => "split",
    };

    private void SetLayout(string mode)
    {
        if (_settings.ShowEditor && _settings.ShowPreview)
            RememberPaneRatio();
        (_settings.ShowEditor, _settings.ShowPreview) = mode switch
        {
            "editor" => (true, false),
            "reader" => (false, true),
            _ => (true, true),
        };
        ApplyLayout();
        UpdateRibbonState();
        if (_settings.ShowEditor)
            Editor.Focus();
        _ = RenderAsync(reparse: false);
    }

    private void ApplyLayout()
    {
        var columns = PaneGrid.ColumnDefinitions;
        var ratio = Math.Clamp(_settings.EditorPaneRatio, 0.15, 0.85);
        var editor = _settings.ShowEditor;
        var preview = _settings.ShowPreview || !editor;

        columns[0].Width = editor ? new GridLength(preview ? ratio : 1, GridUnitType.Star) : new GridLength(0);
        columns[2].Width = preview ? new GridLength(editor ? 1 - ratio : 1, GridUnitType.Star) : new GridLength(0);
        columns[0].MinWidth = editor && preview ? 200 : 0;
        columns[2].MinWidth = editor && preview ? 240 : 0;
        EditorHost.IsVisible = editor;
        Preview.IsVisible = preview;
        PaneSplitter.IsVisible = editor && preview;
    }

    private void RememberPaneRatio()
    {
        var left = EditorHost.Bounds.Width;
        var right = Preview.Bounds.Width;
        if (left > 0 && right > 0)
            _settings.EditorPaneRatio = left / (left + right);
    }

    private void ApplyRibbonCollapsed()
    {
        RibbonBody.IsVisible = !_settings.RibbonCollapsed;
        CollapseRibbonButton.Icon = (Geometry?)this.FindResource(_settings.RibbonCollapsed ? "Icon.ChevronDown" : "Icon.ChevronUp");
        ToolTip.SetTip(CollapseRibbonButton, _settings.RibbonCollapsed ? "Expand the ribbon (Ctrl+F1)" : "Collapse the ribbon (Ctrl+F1)");
    }

    // ============================================================ documents

    private static string LoadGuide()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Markdownia/Assets/Welcome.md"));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void LoadDocument(string text, string? path, bool isGuide = false)
    {
        _filePath = path;
        _isGuide = isGuide;
        Editor.Document = new TextDocument(text);
        Editor.Document.UndoStack.ClearAll();
        Editor.CaretOffset = 0;
        Editor.ScrollToHome();
        _lastParsed = null;
        UpdateTitle();
        UpdateWordCount();
        UpdateCaretStatus();
        _ = RenderAsync(reparse: true);
        Dispatcher.UIThread.Post(() => Preview.ScrollViewer.Offset = default, DispatcherPriority.Background);
    }

    private bool IsDirty => !Editor.Document.UndoStack.IsOriginalFile;

    private string DocumentName => _filePath is not null ? Path.GetFileName(_filePath) : _isGuide ? "Markdown Guide" : "Untitled";

    private void UpdateTitle()
    {
        Title = $"{DocumentName}{(IsDirty ? " •" : "")} — Markdownia";
    }

    private void OpenPath(string path, bool confirm)
    {
        _ = OpenPathAsync(path, confirm);
    }

    private async Task OpenPathAsync(string path, bool confirm)
    {
        if (confirm && !await ConfirmDiscardAsync())
            return;
        try
        {
            var full = Path.GetFullPath(path);
            var text = await File.ReadAllTextAsync(full);
            LoadDocument(text, full);
            AddRecent(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await MessageDialog.ShowAsync(this, "Couldn't open the file", ex.Message);
            _settings.RecentFiles.Remove(path);
        }
    }

    private void AddRecent(string path)
    {
        _settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.Ordinal));
        _settings.RecentFiles.Insert(0, path);
        if (_settings.RecentFiles.Count > 15)
            _settings.RecentFiles.RemoveRange(15, _settings.RecentFiles.Count - 15);
        SettingsStore.Save(_settings);
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty)
            return true;
        var choice = await MessageDialog.AskSaveAsync(this, DocumentName);
        return choice switch
        {
            SaveChoice.Save => await SaveAsync(),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    private static FilePickerFileType MarkdownFileType { get; } = new("Markdown")
    {
        Patterns = MarkdownExtensions.Select(e => "*" + e).ToArray(),
        MimeTypes = ["text/markdown", "text/plain"],
    };

    private async Task<bool> SaveAsync()
    {
        if (_filePath is null)
            return await SaveAsAsync();
        return await WriteFileAsync(_filePath);
    }

    private async Task<bool> SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Markdown document",
            SuggestedFileName = _filePath is not null ? Path.GetFileName(_filePath) : SuggestFileName(),
            DefaultExtension = "md",
            FileTypeChoices = [MarkdownFileType],
            ShowOverwritePrompt = true,
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
            return false;
        if (!await WriteFileAsync(path))
            return false;
        _filePath = path;
        _isGuide = false;
        AddRecent(path);
        UpdateTitle();
        _ = RenderAsync(reparse: false);
        return true;
    }

    private string SuggestFileName()
    {
        var heading = _lastParsed?.Metadata.Title ?? _lastParsed?.Document.Descendants<HeadingBlock>().Select(HeadingText).FirstOrDefault(t => t.Length > 0);
        if (string.IsNullOrWhiteSpace(heading))
            return "Untitled.md";
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(heading.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (name.Length > 60)
            name = name[..60].Trim();
        return (name.Length == 0 ? "Untitled" : name) + ".md";
    }

    private async Task<bool> WriteFileAsync(string path)
    {
        try
        {
            await File.WriteAllTextAsync(path, Editor.Text);
            Editor.Document.UndoStack.MarkAsOriginalFile();
            UpdateTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Couldn't save the file", ex.Message);
            return false;
        }
    }

    private void OnImageResized(ImageResize resize)
    {
        var doc = Editor.Document;
        // Ignore the request if the text has changed since this preview was rendered.
        if (resize.End > doc.TextLength || doc.GetText(resize.Start, resize.End - resize.Start) != resize.Original)
            return;
        if (MarkdownEditing.ImageWidthEdit(doc.Text, resize.Start, resize.End, resize.IsHtml, resize.Width) is { } edit)
        {
            // The edit can move the caret; keep the preview in charge of scrolling while resizing there.
            var leader = _scrollLeader;
            doc.Replace(edit.Offset, edit.Length, edit.Text);
            _scrollLeader = leader;
        }
    }

    private void OnLinkActivated(string url)
    {
        if (url.StartsWith('#'))
        {
            Preview.ScrollToAnchor(Uri.UnescapeDataString(url[1..]));
            return;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && !absolute.IsFile)
        {
            if (absolute.Scheme is "http" or "https" or "mailto" or "ftp")
                _ = Launcher.LaunchUriAsync(absolute);
            return;
        }

        // Relative or file link: open Markdown documents here, everything else with the system handler.
        var baseDir = _filePath is null ? null : Path.GetDirectoryName(_filePath);
        var pathPart = Uri.UnescapeDataString(url.Split('#')[0]);
        if (absolute?.IsFile == true)
            pathPart = absolute.LocalPath;
        if (pathPart.Length == 0 || baseDir is null && !Path.IsPathRooted(pathPart))
            return;
        var full = Path.IsPathRooted(pathPart) ? pathPart : Path.GetFullPath(Path.Combine(baseDir!, pathPart));
        if (!File.Exists(full))
            return;
        if (MarkdownExtensions.Contains(Path.GetExtension(full).ToLowerInvariant()))
            OpenPath(full, confirm: true);
        else
            _ = Launcher.LaunchFileInfoAsync(new FileInfo(full));
    }

    // ============================================================== status

    private void UpdateWordCount()
    {
        var text = Editor.Text ?? "";
        var words = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            var wordChar = char.IsLetterOrDigit(ch) || ch == '\'' || ch == '’';
            if (wordChar && !inWord)
                words++;
            inWord = wordChar;
        }
        WordCountText.Text = words == 1 ? "1 word" : $"{words:N0} words";
        var minutes = Math.Max(1, (int)Math.Round(words / 230.0));
        ReadingTimeText.Text = words == 0 ? "" : $"{minutes} min read";
    }

    private void UpdateCaretStatus()
    {
        var caret = Editor.TextArea.Caret;
        var selection = Editor.SelectionLength;
        CaretText.Text = selection > 0
            ? $"Ln {caret.Line}, Col {caret.Column} ({selection:N0} selected)"
            : $"Ln {caret.Line}, Col {caret.Column}";
    }

    // ============================================================== sizing

    private void ChangePreviewSize(int delta) => SetPreviewSize(_settings.PreviewFontSize + delta);

    private void SetPreviewSize(double size)
    {
        size = Math.Clamp(Math.Round(size), 12, 32);
        if (Math.Abs(size - _settings.PreviewFontSize) < 0.1)
            return;
        _settings.PreviewFontSize = size;
        UpdateRibbonState();
        _ = RenderAsync(reparse: false);
    }

    private void ChangeEditorSize(int delta)
    {
        _settings.EditorFontSize = Math.Clamp(_settings.EditorFontSize + delta, 9, 28);
        ApplyEditorSettings();
    }

    // ======================================================= ribbon handlers

    /// <summary>Editing commands need the editor: reveal it if the reader layout hid it.</summary>
    private void RunEdit(Action action)
    {
        if (!_settings.ShowEditor)
            SetLayout("split");
        action();
        Editor.Focus();
    }

    private void OnNew(object? sender, RoutedEventArgs e) => _ = NewAsync();

    private async Task NewAsync()
    {
        if (await ConfirmDiscardAsync())
            LoadDocument("", null);
    }

    private void OnOpen(object? sender, RoutedEventArgs e) => _ = OpenAsync();

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Markdown document",
            AllowMultiple = false,
            FileTypeFilter = [MarkdownFileType, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await OpenPathAsync(path, confirm: false);
    }

    private void OnSave(object? sender, RoutedEventArgs e) => _ = SaveAsync();

    private void OnSaveAs(object? sender, RoutedEventArgs e) => _ = SaveAsAsync();

    private void OnExportHtml(object? sender, RoutedEventArgs e) => _ = ExportHtmlAsync();

    private async Task ExportHtmlAsync()
    {
        var suggested = Path.ChangeExtension(_filePath is not null ? Path.GetFileName(_filePath) : SuggestFileName(), ".html");
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export as HTML",
            SuggestedFileName = suggested,
            DefaultExtension = "html",
            FileTypeChoices = [new FilePickerFileType("HTML") { Patterns = ["*.html", "*.htm"] }],
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
            return;
        try
        {
            var html = HtmlExporter.Export(Editor.Text ?? "", Path.GetFileNameWithoutExtension(path), ThemeManager.Current, _settings);
            await File.WriteAllTextAsync(path, html);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Couldn't export", ex.Message);
        }
    }

    private void OnShowGuide(object? sender, RoutedEventArgs e) => _ = ShowGuideAsync();

    private async Task ShowGuideAsync()
    {
        if (await ConfirmDiscardAsync())
            LoadDocument(LoadGuide(), null, isGuide: true);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private async void OnMakeDefaultApp(object? sender, RoutedEventArgs e)
    {
        if (await DesktopIntegration.IsDefaultForMarkdownAsync())
        {
            await MessageDialog.ShowAsync(this, "Markdownia", "Markdownia already opens Markdown files.");
            return;
        }
        var error = await DesktopIntegration.SetDefaultForMarkdownAsync();
        await MessageDialog.ShowAsync(this, "Markdownia",
            error is null ? "Markdown files will now open in Markdownia." : "Couldn't change the default application:\n" + error);
    }

    private void OnPaste(object? sender, RoutedEventArgs e) => RunEdit(() => Editor.Paste());

    private void OnCut(object? sender, RoutedEventArgs e) => RunEdit(() => Editor.Cut());

    private void OnCopy(object? sender, RoutedEventArgs e) => RunEdit(() => Editor.Copy());

    private void OnUndo(object? sender, RoutedEventArgs e) => RunEdit(() => Editor.Undo());

    private void OnRedo(object? sender, RoutedEventArgs e) => RunEdit(() => Editor.Redo());

    private void OnSelectAll(object? sender, RoutedEventArgs e) => RunEdit(() => Editor.SelectAll());

    private void OnFind(object? sender, RoutedEventArgs e) => RunEdit(OpenSearch);

    private void OnReplace(object? sender, RoutedEventArgs e) => RunEdit(OpenReplace);

    private void OpenSearch()
    {
        Editor.SearchPanel.IsReplaceMode = false;
        Editor.SearchPanel.Open();
    }

    private void OpenReplace()
    {
        Editor.SearchPanel.Open();
        Editor.SearchPanel.IsReplaceMode = true;
    }

    private void OnFormat(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string tag)
            RunEdit(() => ApplyFormat(tag));
    }

    private void ApplyFormat(string tag)
    {
        switch (tag)
        {
            case "bold": _editing.ToggleWrap("**", "**", "bold text"); break;
            case "italic": _editing.ToggleWrap("*", "*", "italic text"); break;
            case "underline": _editing.ToggleWrap("++", "++", "inserted text"); break;
            case "strike": _editing.ToggleWrap("~~", "~~", "struck text"); break;
            case "code": _editing.ToggleWrap("`", "`", "code"); break;
            case "sup": _editing.ToggleWrap("^", "^", "2"); break;
            case "sub": _editing.ToggleWrap("~", "~", "2"); break;
            case "mark": _editing.ToggleWrap("==", "==", "highlighted text"); break;
            case "math": _editing.InsertInlineMath(); break;
            case "clear": _editing.ClearFormatting(); break;
        }
    }

    private void OnParagraph(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string tag)
            RunEdit(() => ApplyParagraph(tag));
    }

    private void ApplyParagraph(string tag)
    {
        switch (tag)
        {
            case "bullet": _editing.ToggleLineFormat(LineFormat.Bullet); break;
            case "numbered": _editing.ToggleLineFormat(LineFormat.Numbered); break;
            case "task": _editing.ToggleLineFormat(LineFormat.Task); break;
            case "quote": _editing.ToggleLineFormat(LineFormat.Quote); break;
            case "indent": _editing.Indent(outdent: false); break;
            case "outdent": _editing.Indent(outdent: true); break;
            case "codeblock": _editing.InsertCodeBlock(""); break;
            case "rule": _editing.InsertHorizontalRule(); break;
            case "normal": _editing.SetStyle("normal"); break;
        }
    }

    private void OnInsertLink(object? sender, RoutedEventArgs e) => RunEdit(_editing.InsertLink);

    private void OnInsertImage(object? sender, RoutedEventArgs e) => _ = InsertImageAsync();

    private async Task InsertImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert picture",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return;
        var baseDir = _filePath is null ? null : Path.GetDirectoryName(_filePath);
        var reference = baseDir is null ? path : Path.GetRelativePath(baseDir, path);
        RunEdit(() => _editing.InsertImage(reference));
    }

    private void OnInsertMathBlock(object? sender, RoutedEventArgs e) => RunEdit(_editing.InsertMathBlock);

    private void OnInsertFootnote(object? sender, RoutedEventArgs e) => RunEdit(_editing.InsertFootnote);

    private void OnInsertAbbreviation(object? sender, RoutedEventArgs e) => RunEdit(_editing.InsertAbbreviation);

    private void OnInsertDefinition(object? sender, RoutedEventArgs e) => RunEdit(_editing.InsertDefinition);

    private void OnInsertAlert(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string kind)
            return;
        RunEdit(() =>
        {
            if (kind == "div")
                _editing.InsertFencedDiv("note");
            else
                _editing.InsertAlert(kind);
        });
    }

    private void OnInsertDate(object? sender, RoutedEventArgs e) =>
        RunEdit(() => _editing.InsertText(DateTime.Now.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)));

    private void OnLayout(object? sender, RoutedEventArgs e)
    {
        if (_updatingUi)
            return;
        if ((sender as Control)?.Tag is string mode)
            SetLayout(mode);
    }

    private void OnToggleRibbon(object? sender, RoutedEventArgs e)
    {
        _settings.RibbonCollapsed = !_settings.RibbonCollapsed;
        ApplyRibbonCollapsed();
    }

    private void OnPreviewSmaller(object? sender, RoutedEventArgs e) => ChangePreviewSize(-1);

    private void OnPreviewLarger(object? sender, RoutedEventArgs e) => ChangePreviewSize(1);

    private void OnEditorSmaller(object? sender, RoutedEventArgs e) => ChangeEditorSize(-1);

    private void OnEditorLarger(object? sender, RoutedEventArgs e) => ChangeEditorSize(1);

    // ============================================================ keyboard

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None && e.KeyModifiers != KeyModifiers.Shift)
            return;
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                if (_editing.HandleEnter())
                    e.Handled = true;
                break;
            case Key.Tab when _editing.IsInList():
                _editing.Indent(outdent: e.KeyModifiers == KeyModifiers.Shift);
                e.Handled = true;
                break;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        Action? action = null;
        if (ctrl && !alt)
        {
            action = (e.Key, shift) switch
            {
                (Key.N, false) => () => _ = NewAsync(),
                (Key.O, false) => () => _ = OpenAsync(),
                (Key.S, false) => () => _ = SaveAsync(),
                (Key.S, true) => () => _ = SaveAsAsync(),
                (Key.B, false) => () => RunEdit(() => ApplyFormat("bold")),
                (Key.I, false) => () => RunEdit(() => ApplyFormat("italic")),
                (Key.U, false) => () => RunEdit(() => ApplyFormat("underline")),
                (Key.E, false) => () => RunEdit(() => ApplyFormat("code")),
                (Key.M, false) => () => RunEdit(() => ApplyFormat("math")),
                (Key.X, true) => () => RunEdit(() => ApplyFormat("strike")),
                (Key.H, true) => () => RunEdit(() => ApplyFormat("mark")),
                (Key.OemPlus, true) => () => RunEdit(() => ApplyFormat("sup")),
                (Key.OemPlus, false) => () => RunEdit(() => ApplyFormat("sub")),
                (Key.Space, false) => () => RunEdit(() => ApplyFormat("clear")),
                (Key.K, false) => () => RunEdit(_editing.InsertLink),
                (Key.D0 or Key.NumPad0, false) => () => RunEdit(() => _editing.SetStyle("normal")),
                (>= Key.D1 and <= Key.D6, false) => () => RunEdit(() => _editing.SetHeading(e.Key - Key.D0)),
                (Key.D8, true) => () => RunEdit(() => ApplyParagraph("bullet")),
                (Key.D7, true) => () => RunEdit(() => ApplyParagraph("numbered")),
                (Key.D9, true) => () => RunEdit(() => ApplyParagraph("task")),
                (Key.Q, true) => () => RunEdit(() => ApplyParagraph("quote")),
                (Key.F, false) => () => RunEdit(OpenSearch),
                (Key.H, false) => () => RunEdit(OpenReplace),
                (Key.OemCloseBrackets, false) => () => ChangePreviewSize(1),
                (Key.OemOpenBrackets, false) => () => ChangePreviewSize(-1),
                (Key.OemPipe or Key.OemBackslash, false) => () => SetLayout(_settings.ShowEditor ? "reader" : "split"),
                (Key.OemPipe or Key.OemBackslash, true) => () => SetLayout(_settings.ShowPreview && _settings.ShowEditor ? "editor" : "split"),
                (Key.F1, false) => () => OnToggleRibbon(null, new RoutedEventArgs()),
                _ => null,
            };
        }
        else if (ctrl && alt && !shift && e.Key == Key.F)
        {
            action = () => RunEdit(_editing.InsertFootnote);
        }
        else if (alt && !ctrl && e.Key == Key.OemPlus)
        {
            action = () => RunEdit(_editing.InsertMathBlock);
        }

        if (action is not null)
        {
            e.Handled = true;
            action();
        }
    }

    // ========================================================= drag & drop

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (file is null)
            return;
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (MarkdownExtensions.Contains(ext))
        {
            OpenPath(file, confirm: true);
        }
        else if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg")
        {
            var baseDir = _filePath is null ? null : Path.GetDirectoryName(_filePath);
            RunEdit(() => _editing.InsertImage(baseDir is null ? file : Path.GetRelativePath(baseDir, file)));
        }
        e.Handled = true;
    }

    // ============================================================= closing

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && IsDirty)
        {
            e.Cancel = true;
            _ = ConfirmCloseAsync();
            return;
        }
        SaveWindowSettings();
        base.OnClosing(e);
    }

    private async Task ConfirmCloseAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void SaveWindowSettings()
    {
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        if (_settings.ShowEditor && _settings.ShowPreview)
            RememberPaneRatio();
        SettingsStore.Save(_settings);
    }
}
