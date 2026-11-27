using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Shared button appearance and placement for SoftKey popup windows.
static class SoftKeyButtonDesign
{
    static readonly Style ActionFocusStyle = CreateFocusStyle();

    public static Button Apply(bool primary = false)
    {
        var button = Action("Verkställ", primary);
        SetApplyState(button, hasChanges: false);
        return button;
    }

    public static void SetApplyState(Button button, bool hasChanges, bool saving = false)
    {
        button.IsEnabled = hasChanges && !saving;
        button.Opacity = button.IsEnabled ? 1 : 0.55;
        button.Content = saving ? "Sparar…" : "Verkställ";
    }

    public static Button Action(string text, bool primary = false,
        HorizontalAlignment contentAlignment = HorizontalAlignment.Center, bool animateHover = true)
    {
        var button = new Button
        {
            Content = text, Width = 90, Height = 30, FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3),
            Background = primary ? SoftKeyKeyDesign.EnterFace : SoftKeyPopupDesign.CardSurface,
            Foreground = primary ? SoftKeyPopupDesign.OnAccentText : SoftKeyPopupDesign.PrimaryText,
            BorderBrush = primary ? SoftKeyKeyDesign.EnterBorder : SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), FocusVisualStyle = ActionFocusStyle,
            Template = ButtonTemplate(animateHover ? 8 : 4, contentAlignment)
        };
        Brush normal = button.Background;
        Brush border = button.BorderBrush;
        Brush hover = primary ? SoftKeyKeyDesign.EnterHover : SoftKeyPopupDesign.CardHoverSurface;
        Brush pressed = primary ? SoftKeyKeyDesign.EnterPressed : SoftKeyPopupDesign.ActionPressed;

        void ChangeHover(bool hovered)
        {
            SoftKeyHoverTransition.Apply(button, Control.BackgroundProperty, hovered ? hover : normal, animateHover);
            if (animateHover)
                SoftKeyHoverTransition.Apply(button, Control.BorderBrushProperty,
                    hovered && !primary ? SoftKeyPopupDesign.CardHoverBorder : border);
        }

        button.MouseEnter += (_, _) => ChangeHover(true);
        button.PreviewMouseLeftButtonDown += (_, _) => button.Background = pressed;
        button.PreviewMouseLeftButtonUp += (_, _) => ChangeHover(button.IsMouseOver);
        button.MouseLeave += (_, _) => ChangeHover(false);
        return button;
    }

    public static Button Close(Action close)
    {
        var button = new Button
        {
            Content = "×", Width = 46, Height = 36, FontSize = 18,
            Foreground = SoftKeyPopupDesign.PrimaryText,
            Background = SoftKeyPopupDesign.TitleBarSurface, BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Focusable = false, ToolTip = "Stäng", Template = ButtonTemplate(7)
        };
        button.Click += (_, _) => close();
        button.MouseEnter += (_, _) =>
        {
            SoftKeyHoverTransition.Apply(button, Control.BackgroundProperty, SoftKeyPopupDesign.ErrorText);
            SoftKeyHoverTransition.Apply(button, Control.ForegroundProperty, SoftKeyPopupDesign.OnAccentText);
        };
        button.MouseLeave += (_, _) =>
        {
            SoftKeyHoverTransition.Apply(button, Control.BackgroundProperty, SoftKeyPopupDesign.TitleBarSurface);
            SoftKeyHoverTransition.Apply(button, Control.ForegroundProperty, SoftKeyPopupDesign.PrimaryText);
        };
        return button;
    }

    public static StackPanel Row(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i == 0) buttons[i].Margin = new Thickness(0);
            row.Children.Add(buttons[i]);
        }
        return row;
    }

    static Style CreateFocusStyle()
    {
        var outline = new FrameworkElementFactory(typeof(Border));
        outline.SetValue(Border.BorderBrushProperty, SoftKeyPopupDesign.Accent);
        outline.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        outline.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        outline.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = outline }));
        return style;
    }

    static ControlTemplate ButtonTemplate(double radius, HorizontalAlignment contentAlignment = HorizontalAlignment.Center)
    {
        var template = new ControlTemplate(typeof(Button));
        System.Windows.Data.Binding TemplateBinding(string path) => new(path)
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        border.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
        border.SetBinding(Border.BorderBrushProperty, TemplateBinding("BorderBrush"));
        border.SetBinding(Border.BorderThicknessProperty, TemplateBinding("BorderThickness"));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, contentAlignment);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding("Content"));
        content.SetBinding(FrameworkElement.MarginProperty, TemplateBinding("Padding"));
        border.AppendChild(content);
        template.VisualTree = border;
        return template;
    }
}
