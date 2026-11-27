using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

static class SoftKeyTabDesign
{
    static readonly Style HeaderStyle = CreateHeaderStyle();
    static readonly ControlTemplate StripTemplate = CreateStripTemplate();

    public static TabControl Headers(params string[] labels)
    {
        var tabs = new TabControl { Template = StripTemplate };
        foreach (var label in labels) tabs.Items.Add(new TabItem { Header = label, Style = HeaderStyle });
        return tabs;
    }

    public static void ResizePage(FrameworkElement viewport, FrameworkElement page, double width, bool animate = true)
    {
        double oldHeight = viewport.ActualHeight;
        if (!animate || !viewport.IsLoaded || oldHeight <= 0 || SoftKeyHoverTransition.Duration == TimeSpan.Zero)
        {
            viewport.BeginAnimation(FrameworkElement.HeightProperty, null);
            return;
        }

        // The clipped viewport changes height while the page keeps its natural layout.
        page.Measure(new Size(width, double.PositiveInfinity));
        AnimateSize(viewport, FrameworkElement.HeightProperty, oldHeight, page.DesiredSize.Height);
    }

    public static void AnimateSize(FrameworkElement element, DependencyProperty dimension, double from, double to, bool animate = true)
    {
        var duration = SoftKeyHoverTransition.Duration;
        element.BeginAnimation(dimension, animate && element.IsLoaded && from > 0 && from != to && duration != TimeSpan.Zero
            ? new DoubleAnimation(from, to, duration)
            {
                EasingFunction = SoftKeyHoverTransition.Easing(), FillBehavior = FillBehavior.Stop
            }
            : null);
    }

    static ControlTemplate CreateStripTemplate()
    {
        var band = new FrameworkElementFactory(typeof(Border));
        band.SetValue(Border.BorderBrushProperty, SoftKeyPopupDesign.CardBorder);
        band.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        band.SetValue(Border.BackgroundProperty, SoftKeyKeyDesign.TraySurface);
        band.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        band.SetValue(Border.PaddingProperty, new Thickness(4));
        var headers = new FrameworkElementFactory(typeof(UniformGrid));
        headers.SetValue(UniformGrid.RowsProperty, 1);
        headers.SetValue(Panel.IsItemsHostProperty, true);
        headers.SetValue(KeyboardNavigation.TabNavigationProperty, KeyboardNavigationMode.Once);
        band.AppendChild(headers);
        return new ControlTemplate(typeof(TabControl)) { VisualTree = band };
    }

    static Style CreateHeaderStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 38.0));
        style.Setters.Add(new Setter(Control.ForegroundProperty, SoftKeyPopupDesign.PrimaryText));
        var root = new FrameworkElementFactory(typeof(Border), "TabSurface");
        root.SetValue(SoftKeyHoverTransition.BackgroundProperty, Brushes.Transparent);
        root.SetValue(SoftKeyHoverTransition.BorderBrushProperty, Brushes.Transparent);
        root.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        root.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        root.SetValue(Border.MarginProperty, new Thickness(1, 0, 1, 0));
        root.SetValue(Border.PaddingProperty, new Thickness(7, 3, 7, 3));
        var label = new FrameworkElementFactory(typeof(TextBlock), "TabLabel");
        label.SetBinding(TextBlock.TextProperty, new Binding("Header") { RelativeSource = RelativeSource.TemplatedParent });
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4));
        label.SetValue(TextElement.FontSizeProperty, 13.0);
        label.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
        label.SetValue(SoftKeyHoverTransition.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        label.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var face = new FrameworkElementFactory(typeof(Grid));
        face.AppendChild(label);
        var marker = new FrameworkElementFactory(typeof(Border), "SelectionMark");
        marker.SetValue(FrameworkElement.WidthProperty, 26.0);
        marker.SetValue(FrameworkElement.HeightProperty, 2.0);
        marker.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        marker.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        marker.SetValue(Border.BackgroundProperty, SoftKeyPopupDesign.Accent);
        marker.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
        marker.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
        marker.SetValue(UIElement.IsHitTestVisibleProperty, false);
        face.AppendChild(marker);
        root.AppendChild(face);
        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = root };
        var hover = new MultiTrigger();
        hover.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
        hover.Conditions.Add(new Condition(TabItem.IsSelectedProperty, false));
        hover.Setters.Add(new Setter(SoftKeyHoverTransition.ForegroundProperty, SoftKeyPopupDesign.Accent, "TabLabel"));
        hover.Setters.Add(new Setter(SoftKeyHoverTransition.BackgroundProperty, SoftKeyPopupDesign.CardHoverSurface, "TabSurface"));
        hover.Setters.Add(new Setter(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.CardHoverBorder, "TabSurface"));
        template.Triggers.Add(hover);
        var active = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        active.Setters.Add(new Setter(SoftKeyHoverTransition.BackgroundProperty, SoftKeyPopupDesign.CardSurface, "TabSurface"));
        active.Setters.Add(new Setter(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.ActionBorder, "TabSurface"));
        active.Setters.Add(new Setter(SoftKeyHoverTransition.ForegroundProperty, SoftKeyPopupDesign.ActionText, "TabLabel"));
        active.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SelectionMark"));
        template.Triggers.Add(active);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(SoftKeyHoverTransition.BorderBrushProperty, SoftKeyPopupDesign.Accent, "TabSurface"));
        template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}
