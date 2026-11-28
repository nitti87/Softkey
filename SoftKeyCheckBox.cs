using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

// Gemensam checkbox med layout för menyer och inställningsrutor.
class SoftKeyCheckBox : CheckBox
{
    static readonly Brush ToggleOffTrack = Rgb(0xEC, 0xE9, 0xE1);
    static readonly Brush ToggleOffBorder = Rgb(0x94, 0x8F, 0x84);
    static readonly Brush ToggleOffThumb = Rgb(0x6E, 0x69, 0x5F);
    static readonly ControlTemplate MenuTemplate = CreateTemplate(menu: true);
    static readonly ControlTemplate OptionTemplate = CreateTemplate(menu: false);
    static readonly ControlTemplate SettingsRowTemplate = CreateTemplate(menu: false, settingsRow: true);
    static readonly ControlTemplate RadioRowTemplate = CreateTemplate(menu: false, settingsRow: true, radio: true);

    static Brush Rgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    SoftKeyCheckBox()
    {
        Foreground = SoftKeyPopupDesign.PrimaryText;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static SoftKeyCheckBox Menu(string title, double fontSize) => new()
    {
        Content = new TextBlock { Text = title },
        Template = MenuTemplate,
        Padding = SoftKeyPopupDesign.MenuRowPadding,
        Margin = SoftKeyPopupDesign.MenuRowMargin,
        MinHeight = SoftKeyPopupDesign.MenuRowHeight,
        FontSize = fontSize,
        Focusable = false
    };

    public static SoftKeyCheckBox Option(string title, string description) => new()
    {
        Content = OptionContent(title, description),
        Template = OptionTemplate,
        Padding = new Thickness(10, 7, 10, 7),
        FontSize = 13
    };

    public static SoftKeyCheckBox SettingsRow(string title, string description) => SettingsRow(OptionContent(title, description));

    public static SoftKeyCheckBox SettingsRow(UIElement content) => new()
    {
        Content = content, Template = SettingsRowTemplate,
        Padding = new Thickness(10, 10, 10, 10), MinHeight = 56, FontSize = 13,
        Margin = new Thickness(0, 4, 0, 4),
        HorizontalContentAlignment = HorizontalAlignment.Stretch
    };

    public static RadioButton RadioRow(string groupName, UIElement content) => new()
    {
        GroupName = groupName, Content = content, Template = RadioRowTemplate, Foreground = SoftKeyPopupDesign.PrimaryText,
        Padding = new Thickness(10, 7, 10, 7), MinHeight = 46, FontSize = 13,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center
    };

    static UIElement OptionContent(string title, string description)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = description, FontSize = 11,
            Foreground = SoftKeyPopupDesign.SecondaryText,
            Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        return content;
    }

    static ControlTemplate CreateTemplate(bool menu, bool settingsRow = false, bool radio = false)
    {
        var template = new ControlTemplate(radio ? typeof(RadioButton) : typeof(SoftKeyCheckBox));
        Binding TemplateBinding(string property) => new(property)
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        };

        var root = new FrameworkElementFactory(typeof(Border));
        root.Name = "Root";
        var rowBackground = menu ? Border.BackgroundProperty : SoftKeyHoverTransition.BackgroundProperty;
        var rowBorder = menu ? Border.BorderBrushProperty : SoftKeyHoverTransition.BorderBrushProperty;
        root.SetValue(rowBackground, menu ? SoftKeyPopupDesign.MenuSurface : SoftKeyPopupDesign.CardSurface);
        root.SetValue(rowBorder, menu ? Brushes.Transparent : SoftKeyPopupDesign.CardBorder);
        root.SetValue(Border.CornerRadiusProperty, new CornerRadius(menu ? 4 : SoftKeyPopupDesign.CardCornerRadius));
        root.SetValue(Border.BorderThicknessProperty, new Thickness(menu ? 0 : 1));
        root.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        root.SetBinding(Border.PaddingProperty, TemplateBinding("Padding"));

        var row = new FrameworkElementFactory(typeof(DockPanel));
        bool toggleSwitch = settingsRow && !radio;

        var box = new FrameworkElementFactory(typeof(Border));
        box.Name = "Box";
        box.SetValue(DockPanel.DockProperty, settingsRow ? Dock.Right : Dock.Left);
        box.SetValue(FrameworkElement.WidthProperty, toggleSwitch ? 42.0 : radio ? 20.0 : menu ? 18.0 : 20.0);
        box.SetValue(FrameworkElement.HeightProperty, toggleSwitch ? 22.0 : radio ? 20.0 : menu ? 18.0 : 20.0);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        box.SetValue(FrameworkElement.MarginProperty, settingsRow
            ? new Thickness(12, 0, 0, 0)
            : new Thickness(0, 0, menu ? 8 : 10, 0));
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(toggleSwitch ? 11 : radio ? 10 : menu ? 4 : 5));
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        box.SetValue(Border.BorderBrushProperty, settingsRow ? ToggleOffBorder : SoftKeyPopupDesign.ControlOutline);
        box.SetValue(Border.BackgroundProperty, toggleSwitch ? ToggleOffTrack : SoftKeyPopupDesign.CardSurface);

        var mark = new FrameworkElementFactory(toggleSwitch || radio ? typeof(Ellipse) : typeof(Path));
        mark.Name = "Mark";
        mark.SetValue(FrameworkElement.WidthProperty, toggleSwitch ? 14.0 : radio ? 8.0 : 13.0);
        mark.SetValue(FrameworkElement.HeightProperty, toggleSwitch ? 14.0 : radio ? 8.0 : 10.0);
        mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, toggleSwitch ? HorizontalAlignment.Left : HorizontalAlignment.Center);
        mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        if (toggleSwitch)
        {
            mark.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 0, 0));
            mark.SetValue(Shape.FillProperty, ToggleOffThumb);
        }
        else if (radio)
        {
            mark.SetValue(Shape.FillProperty, SoftKeyPopupDesign.OnAccentText);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        }
        else
        {
            mark.SetValue(Path.DataProperty, Geometry.Parse("M 0,4 L 3.5,7.5 L 10,0"));
            mark.SetValue(Path.StrokeProperty, SoftKeyPopupDesign.OnAccentText);
            mark.SetValue(Path.StrokeThicknessProperty, 2.0);
            mark.SetValue(Path.StretchProperty, Stretch.Uniform);
            mark.SetValue(Path.StrokeStartLineCapProperty, PenLineCap.Round);
            mark.SetValue(Path.StrokeEndLineCapProperty, PenLineCap.Round);
            mark.SetValue(Path.StrokeLineJoinProperty, PenLineJoin.Round);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        }
        box.AppendChild(mark);
        row.AppendChild(box);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding("Content"));
        row.AppendChild(content);
        root.AppendChild(row);
        template.VisualTree = root;

        var checkedState = new Trigger { Property = IsCheckedProperty, Value = true };
        if (toggleSwitch)
        {
            checkedState.Setters.Add(new Setter(FrameworkElement.WidthProperty, 16.0, "Mark"));
            checkedState.Setters.Add(new Setter(FrameworkElement.HeightProperty, 16.0, "Mark"));
            checkedState.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Mark"));
            checkedState.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 0), "Mark"));
            checkedState.Setters.Add(new Setter(Shape.FillProperty, SoftKeyPopupDesign.OnAccentText, "Mark"));
        }
        else
        {
            checkedState.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Mark"));
        }
        checkedState.Setters.Add(new Setter(Border.BackgroundProperty, SoftKeyPopupDesign.Accent, "Box"));
        checkedState.Setters.Add(new Setter(Border.BorderBrushProperty, SoftKeyPopupDesign.Accent, "Box"));
        if (radio)
            checkedState.Setters.Add(new Setter(rowBorder, SoftKeyPopupDesign.ActionBorder, "Root"));
        else if (!menu && !settingsRow)
        {
            checkedState.Setters.Add(new Setter(rowBackground, SoftKeyPopupDesign.FieldSelected, "Root"));
            checkedState.Setters.Add(new Setter(rowBorder, SoftKeyPopupDesign.ActionBorder, "Root"));
        }
        template.Triggers.Add(checkedState);

        var hovered = new MultiTrigger { Conditions = { new(IsMouseOverProperty, true), new(IsEnabledProperty, true) } };
        hovered.Setters.Add(new Setter(rowBackground, menu ? SoftKeyPopupDesign.MenuHover : SoftKeyPopupDesign.CardHoverSurface, "Root"));
        if (!menu) hovered.Setters.Add(new Setter(rowBorder, SoftKeyPopupDesign.CardHoverBorder, "Root"));
        template.Triggers.Add(hovered);

        var pressed = new Trigger { Property = IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(rowBackground, menu ? SoftKeyPopupDesign.MenuPressed : SoftKeyPopupDesign.ActionPressed, "Root"));
        template.Triggers.Add(pressed);

        var focused = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(rowBorder, SoftKeyPopupDesign.Accent, "Root"));
        template.Triggers.Add(focused);

        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "Root"));
        template.Triggers.Add(disabled);
        return template;
    }
}
