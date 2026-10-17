using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Markdownia.Controls;

public enum RibbonButtonSize
{
    /// <summary>Tall button: large icon with the label underneath (Word's "Paste").</summary>
    Large,

    /// <summary>Compact row button: small icon with the label to the right.</summary>
    Small,

    /// <summary>Icon only; the label is shown as the tooltip.</summary>
    Icon,
}

/// <summary>Shared attached properties so buttons and toggle buttons can use one look.</summary>
public static class Ribbon
{
    public static readonly AttachedProperty<Geometry?> IconProperty =
        AvaloniaProperty.RegisterAttached<Control, Geometry?>("Icon", typeof(Ribbon));

    public static readonly AttachedProperty<string?> LabelProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Label", typeof(Ribbon));

    public static readonly AttachedProperty<RibbonButtonSize> SizeProperty =
        AvaloniaProperty.RegisterAttached<Control, RibbonButtonSize>("Size", typeof(Ribbon), RibbonButtonSize.Small);

    public static readonly AttachedProperty<bool> ShowChevronProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ShowChevron", typeof(Ribbon));

    public static Geometry? GetIcon(Control c) => c.GetValue(IconProperty);
    public static void SetIcon(Control c, Geometry? v) => c.SetValue(IconProperty, v);
    public static string? GetLabel(Control c) => c.GetValue(LabelProperty);
    public static void SetLabel(Control c, string? v) => c.SetValue(LabelProperty, v);
    public static RibbonButtonSize GetSize(Control c) => c.GetValue(SizeProperty);
    public static void SetSize(Control c, RibbonButtonSize v) => c.SetValue(SizeProperty, v);
    public static bool GetShowChevron(Control c) => c.GetValue(ShowChevronProperty);
    public static void SetShowChevron(Control c, bool v) => c.SetValue(ShowChevronProperty, v);
}

public class RibbonButton : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty = Ribbon.IconProperty.AddOwner<RibbonButton>();
    public static readonly StyledProperty<string?> LabelProperty = Ribbon.LabelProperty.AddOwner<RibbonButton>();
    public static readonly StyledProperty<RibbonButtonSize> SizeProperty = Ribbon.SizeProperty.AddOwner<RibbonButton>();
    public static readonly StyledProperty<bool> ShowChevronProperty = Ribbon.ShowChevronProperty.AddOwner<RibbonButton>();

    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public RibbonButtonSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public bool ShowChevron { get => GetValue(ShowChevronProperty); set => SetValue(ShowChevronProperty, value); }

    protected override Type StyleKeyOverride => typeof(RibbonButton);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty || change.Property == LabelProperty)
            UpdatePseudoClasses();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":large", Size == RibbonButtonSize.Large);
        PseudoClasses.Set(":iconOnly", Size == RibbonButtonSize.Icon || string.IsNullOrEmpty(Label));
        if (Size == RibbonButtonSize.Icon && ToolTip.GetTip(this) is null && Label is not null)
            ToolTip.SetTip(this, Label);
    }
}

public class RibbonToggleButton : ToggleButton
{
    public static readonly StyledProperty<Geometry?> IconProperty = Ribbon.IconProperty.AddOwner<RibbonToggleButton>();
    public static readonly StyledProperty<string?> LabelProperty = Ribbon.LabelProperty.AddOwner<RibbonToggleButton>();
    public static readonly StyledProperty<RibbonButtonSize> SizeProperty = Ribbon.SizeProperty.AddOwner<RibbonToggleButton>();

    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public RibbonButtonSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    protected override Type StyleKeyOverride => typeof(RibbonToggleButton);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty || change.Property == LabelProperty)
            UpdatePseudoClasses();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":large", Size == RibbonButtonSize.Large);
        PseudoClasses.Set(":iconOnly", Size == RibbonButtonSize.Icon || string.IsNullOrEmpty(Label));
        if (Size == RibbonButtonSize.Icon && ToolTip.GetTip(this) is null && Label is not null)
            ToolTip.SetTip(this, Label);
    }
}

/// <summary>A labeled cluster of ribbon commands with a separator on its right edge.</summary>
public class RibbonGroup : HeaderedContentControl
{
    protected override Type StyleKeyOverride => typeof(RibbonGroup);
}
