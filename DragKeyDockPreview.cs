using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

// A noninteractive outline of the existing toolbar design, not a second set of buttons.
sealed class DragKeyDockPreview : Border
{
    readonly UniformGrid symbols = new();
    readonly Border panelSurface;
    readonly Border rightDrag = PreviewButton(DragKeySymbol.RightDrag), middleDrag = PreviewButton(DragKeySymbol.MiddleDrag);
    readonly Border moveKeyboardEdge = PreviewButton(DragKeySymbol.KeyboardToTop);
    readonly bool autoClickOnly;
    DragKeySize size = DragKeySize.Normal;
    bool keyboardMovesToTop = true;

    public DragKeyDockPreview(bool autoClickOnly = false)
    {
        this.autoClickOnly = autoClickOnly;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        var body = new Grid { Opacity = 0.6 };
        symbols.Children.Add(PreviewButton(DragKeySymbol.AutoClick, autoClickOnly));
        if (autoClickOnly) size = DragKeySize.SignInNormal;
        else
        {
            foreach (var icon in new[] { PreviewButton(DragKeySymbol.RightClick), PreviewButton(DragKeySymbol.DoubleClick),
                PreviewButton(DragKeySymbol.LeftDrag), rightDrag, middleDrag, PreviewButton(DragKeySymbol.Keyboard), PreviewButton(DragKeySymbol.WindowHeader),
                moveKeyboardEdge, PreviewButton(DragKeySymbol.Undock), PreviewButton(DragKeySymbol.Power) })
                symbols.Children.Add(icon);
        }
        SetDragOptions(false, false);
        body.Children.Add(symbols);
        panelSurface = new Border
        {
            CornerRadius = DragKeyWindow.PanelCornerRadius,
            BorderBrush = SoftKeyKeyDesign.SlateAccent, BorderThickness = new Thickness(1),
            Background = SoftKeyKeyDesign.WindowSurface, Child = body
        };
        Child = panelSurface;
    }

    internal static Border PreviewButton(DragKeySymbol symbol, bool signIn = false) => new()
    {
        CornerRadius = DragKeyButton.FaceCornerRadius,
        Background = DragKeyButton.FaceFor(symbol),
        BorderBrush = SoftKeyKeyDesign.Outline,
        BorderThickness = new Thickness(1), Child = DragKeyButton.CreateSymbol(symbol, signIn: signIn)
    };

    public void SetDragOptions(bool right, bool middle)
    {
        rightDrag.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
        middleDrag.Visibility = middle ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetSmallSize(bool small)
    {
        if (autoClickOnly) return;
        size = DragKeySize.FromSetting(small);
        foreach (Border symbol in symbols.Children)
            DragKeyButton.SetIconSize((Viewbox)symbol.Child, small);
    }

    public void SetKeyboardMoveDestination(bool toTop)
    {
        if (keyboardMovesToTop == toTop) return;
        keyboardMovesToTop = toTop;
        var artwork = DragKeyButton.CreateSymbol(toTop ? DragKeySymbol.KeyboardToTop : DragKeySymbol.KeyboardToBottom);
        artwork.LayoutTransform = ((Viewbox)moveKeyboardEdge.Child).LayoutTransform;
        moveKeyboardEdge.Child = artwork;
    }

    public void SetPlacement(DragKeyPlacement placement)
    {
        bool vertical = !autoClickOnly && placement is (DragKeyPlacement.Left or DragKeyPlacement.Right);
        symbols.Margin = size.PanelMargin;
        symbols.Columns = vertical ? DragKeyDocking.SideColumns : 0;
        symbols.Rows = vertical ? 0 : 1;
        foreach (Border symbol in symbols.Children)
            size.FitCell(symbol, vertical);
        panelSurface.Width = vertical ? size.SideWidth : size.HorizontalWidth;
        panelSurface.Height = vertical
            ? size.SideHeight(rightDrag.Visibility == Visibility.Visible, middleDrag.Visibility == Visibility.Visible)
            : size.TopHeight;
    }

    public void SetDockScale(double scale)
    {
        if (panelSurface.LayoutTransform.Value.M11 == scale) return;
        panelSurface.LayoutTransform = scale == 1 ? System.Windows.Media.Transform.Identity : new System.Windows.Media.ScaleTransform(scale, scale);
        foreach (Border symbol in symbols.Children)
            ((Viewbox)symbol.Child).Child.InvalidateVisual();
    }
}
