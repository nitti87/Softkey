using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

enum DragKeySymbol { AutoClick, RightClick, DoubleClick, LeftDrag, MiddleDrag, Keyboard, WindowHeader, Undock, RightDrag, Power, KeyboardToTop, KeyboardToBottom, AutoClickPaused }

// Compact vector-only buttons; names and state remain available to assistive tools.
sealed class DragKeyButton : Button
{
    static readonly ControlTemplate ButtonTemplate = CreateTemplate();
    static readonly ScaleTransform SmallIconScale = new(0.85, 0.85);
    static readonly DependencyProperty FaceInsetProperty = DependencyProperty.Register(
        nameof(FaceInset), typeof(Thickness), typeof(DragKeyButton), new PropertyMetadata(new Thickness()));
    readonly bool signIn;
    DragKeySymbol symbol;
    public Thickness FaceInset => (Thickness)GetValue(FaceInsetProperty);
    public Brush RestingFace { get; }

    public DragKeyButton(string label, DragKeySymbol symbol, bool signIn = false)
    {
        this.symbol = symbol;
        this.signIn = signIn;
        FitTarget(DragKeySize.Normal, vertical: false);
        Padding = signIn ? new Thickness(3) : new Thickness(0);
        BorderThickness = new Thickness(1);
        Focusable = false;
        Cursor = System.Windows.Input.Cursors.Arrow;
        UseLayoutRounding = SnapsToDevicePixels = true;
        Template = ButtonTemplate;
        RestingFace = FaceFor(symbol);

        var artwork = CreateSymbol(symbol, followsButtonState: true, signIn: signIn);
        Content = artwork;
        System.Windows.Automation.AutomationProperties.SetName(this, label);
    }

    public static Viewbox CreateSymbol(DragKeySymbol symbol, bool followsButtonState = false, bool signIn = false)
    {
        var artwork = new DragKeyIconImage(symbol)
        {
            Width = DragKeySymbols.Width, Height = DragKeySymbols.Height, IsHitTestVisible = false
        };
        if (followsButtonState)
        {
            var style = new Style(typeof(Image));
            style.Setters.Add(new Setter(Image.SourceProperty, DragKeySymbols.Image(symbol)));
            var paused = new DataTrigger
            {
                Binding = new Binding(nameof(IsEnabled))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DragKeyButton), 1)
                },
                Value = false
            };
            paused.Setters.Add(new Setter(Image.SourceProperty, DragKeySymbols.Image(symbol, paused: true)));
            style.Triggers.Add(paused);
            artwork.Style = style;
        }
        else artwork.Source = DragKeySymbols.Image(symbol);
        double length = signIn ? 18 : 22;
        return new Viewbox
        {
            Width = length, Height = length, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = artwork, IsHitTestVisible = false
        };
    }

    public static void SetIconSize(Viewbox icon, bool small)
    {
        icon.LayoutTransform = small ? SmallIconScale : Transform.Identity;
        icon.Child.InvalidateVisual();
    }

    public void FitTarget(DragKeySize size, bool vertical)
    {
        size.FitCell(this, vertical);
        if (signIn || symbol is not (DragKeySymbol.AutoClick or DragKeySymbol.AutoClickPaused
            or DragKeySymbol.RightClick or DragKeySymbol.DoubleClick or DragKeySymbol.LeftDrag
            or DragKeySymbol.RightDrag or DragKeySymbol.MiddleDrag)) return;

        // Use the whole cell for input and keep the visible face at its original size.
        var inset = Margin;
        SetValue(FaceInsetProperty, inset);
        Height += inset.Top + inset.Bottom;
        Margin = new Thickness();
    }

    public void ChangeSymbol(DragKeySymbol symbol)
    {
        if (this.symbol == symbol) return;
        this.symbol = symbol;
        var artwork = CreateSymbol(symbol, followsButtonState: true, signIn: signIn);
        artwork.LayoutTransform = ((Viewbox)Content).LayoutTransform;
        Content = artwork;
    }

    public static Brush FaceFor(DragKeySymbol symbol) => symbol switch
    {
        DragKeySymbol.AutoClick or DragKeySymbol.AutoClickPaused => SoftKeyKeyDesign.AccentFace,
        DragKeySymbol.RightClick or DragKeySymbol.DoubleClick or DragKeySymbol.LeftDrag
            or DragKeySymbol.RightDrag or DragKeySymbol.MiddleDrag => SoftKeyKeyDesign.LetterFace,
        _ => SoftKeyKeyDesign.FunctionFace
    };

    static ControlTemplate CreateTemplate()
    {
        var template = new ControlTemplate(typeof(DragKeyButton));
        var border = new FrameworkElementFactory(typeof(Border), "Frame");
        foreach (var pair in new[]
        {
            (Border.BackgroundProperty, "Background"), (Border.BorderBrushProperty, "BorderBrush"),
            (Border.BorderThicknessProperty, "BorderThickness")
        })
            border.SetBinding(pair.Item1, new System.Windows.Data.Binding(pair.Item2)
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
        var face = new FrameworkElementFactory(typeof(Grid));
        var highlight = new FrameworkElementFactory(typeof(Border), "PointerHighlight");
        highlight.SetValue(Border.BackgroundProperty, SoftKeyKeyDesign.SlateAccent);
        highlight.SetValue(IsHitTestVisibleProperty, false);
        highlight.SetValue(OpacityProperty, 0d);
        face.AppendChild(highlight);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(MarginProperty, new Binding(nameof(Padding)) { RelativeSource = RelativeSource.TemplatedParent });
        face.AppendChild(content);
        border.AppendChild(face);
        var faceWithShadow = SoftKeyKeyDesign.Raised(border, shadowRadius: new CornerRadius(0));
        faceWithShadow.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(FaceInset))
        {
            RelativeSource = RelativeSource.TemplatedParent
        });
        var target = new FrameworkElementFactory(typeof(Border));
        target.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        target.AppendChild(faceWithShadow);
        template.VisualTree = target;
        template.Triggers.Add(PointerState(IsMouseOverProperty, 0.14));
        template.Triggers.Add(PointerState(ButtonBase.IsPressedProperty, 0.24));
        return template;

        static MultiTrigger PointerState(DependencyProperty property, double opacity)
        {
            var state = new MultiTrigger();
            state.Conditions.Add(new Condition(IsEnabledProperty, true));
            state.Conditions.Add(new Condition(property, true));
            state.Setters.Add(new Setter(Border.BorderBrushProperty, SoftKeyKeyDesign.SlateAccent, "Frame"));
            state.Setters.Add(new Setter(OpacityProperty, opacity, "PointerHighlight"));
            return state;
        }
    }
}
