using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Shared button appearance and placement for SoftKey popup windows.
static class SoftKeyButtonDesign
{
    static readonly Brush CloseText = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
    static readonly Brush CloseHover = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));

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
            Background = primary ? SoftKeyPopupDesign.ActionSurface : SoftKeyPopupDesign.CardSurface,
            Foreground = primary ? SoftKeyPopupDesign.ActionText : SoftKeyPopupDesign.PrimaryText,
            BorderBrush = primary ? SoftKeyPopupDesign.ActionBorder : SoftKeyPopupDesign.CardBorder,
            Template = ButtonTemplate(animateHover ? 8 : 4, contentAlignment)
        };
        Brush normal = button.Background;
        Brush border = button.BorderBrush;
        Brush hover = primary ? SoftKeyPopupDesign.ActionHover : SoftKeyPopupDesign.CardHoverSurface;

        void ChangeHover(bool hovered)
        {
            SoftKeyHoverTransition.Apply(button, Control.BackgroundProperty, hovered ? hover : normal, animateHover);
            if (animateHover)
                SoftKeyHoverTransition.Apply(button, Control.BorderBrushProperty,
                    hovered ? SoftKeyPopupDesign.CardHoverBorder : border);
        }

        button.MouseEnter += (_, _) => ChangeHover(true);
        button.PreviewMouseLeftButtonDown += (_, _) => button.Background = SoftKeyPopupDesign.ActionPressed;
        button.PreviewMouseLeftButtonUp += (_, _) => ChangeHover(button.IsMouseOver);
        button.MouseLeave += (_, _) => ChangeHover(false);
        return button;
    }

    public static Button Close(Action close)
    {
        var button = new Button
        {
            Content = "×", Width = 46, Height = 36, FontSize = 18,
            Foreground = CloseText,
            Background = SoftKeyPopupDesign.TitleBarSurface, BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Focusable = false, ToolTip = "Stäng", Template = ButtonTemplate(7)
        };
        button.Click += (_, _) => close();
        button.MouseEnter += (_, _) =>
        {
            SoftKeyHoverTransition.Apply(button, Control.BackgroundProperty, CloseHover);
            SoftKeyHoverTransition.Apply(button, Control.ForegroundProperty, Brushes.White);
        };
        button.MouseLeave += (_, _) =>
        {
            SoftKeyHoverTransition.Apply(button, Control.BackgroundProperty, SoftKeyPopupDesign.TitleBarSurface);
            SoftKeyHoverTransition.Apply(button, Control.ForegroundProperty, CloseText);
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
