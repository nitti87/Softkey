using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

static class SoftKeySettingsDesign
{
    public const double Width = 488;
    public const string LockGlyph = "M 6,10 V 7 A 4,4 0 0 1 14,7 V 10 M 6,10 H 14 Q 16,10 16,12 V 16 Q 16,18 14,18 H 6 Q 4,18 4,16 V 12 Q 4,10 6,10 Z M 10,13 V 15";
    static readonly Brush InactiveTileBrush = FrozenBrush(0xE2, 0xDF, 0xD7);
    static readonly Brush InactiveGlyphBrush = FrozenBrush(0x5C, 0x58, 0x50);
    static readonly Style IconTileStyle = CreateAncestorCheckedStyle(typeof(Border),
        Border.BackgroundProperty, InactiveTileBrush, SoftKeyPopupDesign.ActionSurface);
    static readonly Style IconGlyphStyle = CreateAncestorCheckedStyle(typeof(Path),
        Shape.StrokeProperty, InactiveGlyphBrush, SoftKeyPopupDesign.Accent);

    static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    static Style CreateAncestorCheckedStyle(Type targetType, DependencyProperty property, Brush uncheckedBrush, Brush checkedBrush)
    {
        var style = new Style(targetType);
        style.Setters.Add(new Setter(property, uncheckedBrush));
        var checkedTrigger = new DataTrigger
        {
            Binding = new Binding
            {
                Path = new PropertyPath(ToggleButton.IsCheckedProperty),
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1),
                FallbackValue = false
            },
            Value = true
        };
        checkedTrigger.Setters.Add(new Setter(property, checkedBrush));
        style.Triggers.Add(checkedTrigger);
        style.Seal();
        return style;
    }

    public static TextBlock Heading(string label) => new()
    {
        Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 0, 0, 8)
    };

    public static SoftKeyCheckBox CheckOption(string label, string detail, UIElement icon)
    {
        var option = SoftKeyCheckBox.SettingsRow(OptionContent(label, detail, icon));
        FormatOption(option, label, detail);
        return option;
    }

    public static RadioButton RadioOption(string group, string label, string detail, UIElement icon)
    {
        var option = SoftKeyCheckBox.RadioRow(group, OptionContent(label, detail, icon));
        FormatOption(option, label, detail);
        return option;
    }

    static void FormatOption(ToggleButton option, string label, string detail)
    {
        option.MinHeight = 68;
        option.Padding = new Thickness(13, 10, 14, 10);
        option.Margin = new Thickness(0);
        AutomationProperties.SetName(option, label);
        AutomationProperties.SetHelpText(option, detail);
    }

    static DockPanel OptionContent(string label, string detail, UIElement icon)
    {
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = detail, FontSize = 12, Foreground = SoftKeyPopupDesign.SecondaryText,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2.5, 0, 0)
                }
            }
        };
        DockPanel.SetDock(icon, Dock.Left);
        return new DockPanel { Children = { icon, text } };
    }

    public static Border Icon(string outline) => Icon(new Path
    {
        Data = Geometry.Parse(outline), Style = IconGlyphStyle, StrokeThickness = 1.6,
        Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    });

    public static Border Icon(UIElement artwork) => new()
    {
        MinWidth = 38, Height = 38, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 0, 7, 0),
        Margin = new Thickness(0, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center,
        Style = IconTileStyle, Child = artwork, IsHitTestVisible = false
    };

    public static Border Card(UIElement content) => new()
    {
        Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
        Padding = new Thickness(12, 10, 12, 10), Child = content
    };

    public static void FitDialog(Window window, StackPanel panel, ScrollViewer scroll, double footerReserve, params Button[] actions)
    {
        SoftKeyPopupDesign.ConfigureWindow(window, available =>
        {
            panel.Width = Math.Min(Width, Math.Max(280, available.Width - 20));
            scroll.MaxHeight = Math.Max(80, available.Height - footerReserve);
            foreach (var action in actions) action.Width = Math.Min(90, (panel.Width - 44) / actions.Length);
        });
    }
}
