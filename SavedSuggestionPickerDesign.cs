using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

static class SavedSuggestionPickerDesign
{
    static readonly Style CardStyle = CreateCardStyle();

    public static void Configure(Window window, UIElement body)
    {
        SoftKeyPopupDesign.ConfigureWindow(window);
        window.ShowInTaskbar = false;
        var paddedBody = new Border
        {
            Width = 478, Padding = new Thickness(8, 5, 8, 7), Child = body
        };
        window.Content = SoftKeyPopupDesign.Frame(window.Title, paddedBody, window.Close, window.DragMove);
    }

    public static ScrollViewer ScrollCards(Panel rows) => new()
    {
        Content = rows, MaxHeight = 278, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false
    };

    public static Button Card(string title, UIElement icon, string detail = null)
    {
        var mark = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(13),
            Background = SoftKeyPopupDesign.ActionSurface, Margin = new Thickness(0, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon
        };
        var arrow = new Path
        {
            Data = Geometry.Parse("M 3,2 L 7,6 L 3,10"), Width = 10, Height = 11,
            Stroke = SoftKeyPopupDesign.ActionText, StrokeThickness = 1.25, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.Uniform,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(arrow, Dock.Right);
        var content = new DockPanel { IsHitTestVisible = false };
        content.Children.Add(mark);
        content.Children.Add(arrow);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = title, TextWrapping = TextWrapping.Wrap, Foreground = SoftKeyPopupDesign.PrimaryText,
            FontSize = 15, FontWeight = FontWeights.SemiBold
        });
        if (detail != null)
            labels.Children.Add(new TextBlock
            {
                Text = detail, TextWrapping = TextWrapping.Wrap, Foreground = SoftKeyPopupDesign.ActionText, FontSize = 12
            });
        content.Children.Add(labels);

        var button = new Button { Style = CardStyle, Content = content };
        AutomationProperties.SetName(button, detail == null ? title : $"{title}, {detail}");
        return button;
    }

    static Style CreateCardStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SoftKeyPopupDesign.CardSurface));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, SoftKeyPopupDesign.CardBorder));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 13, 5)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 46.0));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(7, 4, 7, 4)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

        const string hoverLayerName = "HoverLayer";
        var template = new ControlTemplate(typeof(Button));
        var layers = new FrameworkElementFactory(typeof(Grid));
        var card = new FrameworkElementFactory(typeof(Border));
        card.SetValue(Border.CornerRadiusProperty, new CornerRadius(SoftKeyPopupDesign.CardCornerRadius));
        card.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        card.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        card.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        layers.AppendChild(card);

        var hoverLayer = new FrameworkElementFactory(typeof(Border), hoverLayerName);
        hoverLayer.SetValue(Border.CornerRadiusProperty, new CornerRadius(SoftKeyPopupDesign.CardCornerRadius));
        hoverLayer.SetValue(Border.BackgroundProperty, SoftKeyPopupDesign.CardHoverSurface);
        hoverLayer.SetValue(Border.BorderBrushProperty, SoftKeyPopupDesign.CardHoverBorder);
        hoverLayer.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        hoverLayer.SetValue(UIElement.IsHitTestVisibleProperty, false);
        hoverLayer.SetValue(UIElement.OpacityProperty, 0.0);
        hoverLayer.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            Color = ((SolidColorBrush)SoftKeyPopupDesign.ActionText).Color, Opacity = 0.28, BlurRadius = 9,
            ShadowDepth = 3, Direction = 270
        });
        layers.AppendChild(hoverLayer);

        var contentFrame = new FrameworkElementFactory(typeof(Border));
        contentFrame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        contentFrame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        contentFrame.AppendChild(content);
        layers.AppendChild(contentFrame);
        template.VisualTree = layers;

        template.Triggers.Add(new MultiTrigger
        {
            Conditions = { new(UIElement.IsMouseOverProperty, true), new(UIElement.IsEnabledProperty, true) },
            Setters = { new Setter(Panel.ZIndexProperty, 1) },
            EnterActions = { FadeHoverLayer(1) },
            ExitActions = { FadeHoverLayer(0) }
        });
        template.Triggers.Add(new Trigger
        {
            Property = Button.IsPressedProperty, Value = true,
            Setters = { new Setter(Border.BackgroundProperty, SoftKeyPopupDesign.ActionHover, hoverLayerName) }
        });
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        style.Triggers.Add(new Trigger
        {
            Property = UIElement.IsKeyboardFocusedProperty, Value = true,
            Setters = { new Setter(Control.BorderBrushProperty, SoftKeyPopupDesign.Accent) }
        });
        style.Triggers.Add(new Trigger
        {
            Property = Button.IsPressedProperty, Value = true,
            Setters = { new Setter(Control.BackgroundProperty, SoftKeyPopupDesign.ActionHover) }
        });
        style.Triggers.Add(new Trigger
        {
            Property = UIElement.IsEnabledProperty, Value = false,
            Setters = { new Setter(UIElement.OpacityProperty, 0.56) }
        });
        return style;

        BeginStoryboard FadeHoverLayer(double opacity)
        {
            var fade = new DoubleAnimation
            {
                To = opacity, Duration = SoftKeyHoverTransition.Duration,
                EasingFunction = SoftKeyHoverTransition.Easing()
            };
            Storyboard.SetTargetName(fade, hoverLayerName);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            return new BeginStoryboard { Storyboard = new Storyboard { Children = { fade } } };
        }
    }
}
