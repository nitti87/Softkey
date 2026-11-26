using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

// Gemensam checkbox med layout för menyer och inställningsrutor.
class SoftKeyCheckBox : CheckBox
{
    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x29, 0x39, 0x48));
    static readonly Brush Outline = new SolidColorBrush(Color.FromRgb(0x72, 0x7C, 0x87));
    static readonly ControlTemplate MenuTemplate = CreateTemplate(menu: true);
    static readonly ControlTemplate OptionTemplate = CreateTemplate(menu: false);
    static readonly ControlTemplate SettingsRowTemplate = CreateTemplate(menu: false, settingsRow: true);
    static readonly ControlTemplate RadioRowTemplate = CreateTemplate(menu: false, settingsRow: true, radio: true);

    SoftKeyCheckBox()
    {
        Foreground = Ink;
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
        GroupName = groupName, Content = content, Template = RadioRowTemplate, Foreground = Ink,
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x59, 0x62)),
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

        var box = new FrameworkElementFactory(typeof(Border));
        box.Name = "Box";
        box.SetValue(DockPanel.DockProperty, Dock.Left);
        box.SetValue(FrameworkElement.WidthProperty, menu || settingsRow ? 18.0 : 20.0);
        box.SetValue(FrameworkElement.HeightProperty, menu || settingsRow ? 18.0 : 20.0);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, settingsRow ? 12 : menu ? 8 : 10, 0));
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(radio ? 9 : settingsRow ? 3 : menu ? 4 : 5));
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        box.SetValue(Border.BorderBrushProperty, Outline);
        if (menu || settingsRow) box.SetValue(Border.BackgroundProperty, Brushes.White);

        var mark = new FrameworkElementFactory(radio ? typeof(Ellipse) : typeof(Path));
        mark.Name = "Mark";
        mark.SetValue(FrameworkElement.WidthProperty, radio ? 8.0 : 13.0);
        mark.SetValue(FrameworkElement.HeightProperty, radio ? 8.0 : 10.0);
        mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        if (radio) mark.SetValue(Shape.FillProperty, Brushes.White);
        else
        {
            mark.SetValue(Path.DataProperty, Geometry.Parse("M 0,4 L 3.5,7.5 L 10,0"));
            mark.SetValue(Path.StrokeProperty, Brushes.White);
            mark.SetValue(Path.StrokeThicknessProperty, 2.0);
            mark.SetValue(Path.StretchProperty, Stretch.Uniform);
            mark.SetValue(Path.StrokeStartLineCapProperty, PenLineCap.Round);
            mark.SetValue(Path.StrokeEndLineCapProperty, PenLineCap.Round);
            mark.SetValue(Path.StrokeLineJoinProperty, PenLineJoin.Round);
        }
        mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        box.AppendChild(mark);
        row.AppendChild(box);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding("Content"));
        row.AppendChild(content);
        root.AppendChild(row);
        template.VisualTree = root;

        var checkedState = new Trigger { Property = IsCheckedProperty, Value = true };
        checkedState.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Mark"));
        checkedState.Setters.Add(new Setter(Border.BackgroundProperty, Ink, "Box"));
        checkedState.Setters.Add(new Setter(Border.BorderBrushProperty, Ink, "Box"));
        if (!menu)
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
        focused.Setters.Add(new Setter(rowBorder, menu ? Ink : SoftKeyPopupDesign.Accent, "Root"));
        template.Triggers.Add(focused);

        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "Root"));
        template.Triggers.Add(disabled);
        return template;
    }
}
