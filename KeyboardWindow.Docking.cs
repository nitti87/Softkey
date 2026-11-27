using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

partial class KeyboardWindow
{
    const double DesktopMenuRowHeight = 30;
    Border dragKeyDockHost;
    FrameworkElement keyboardSurface;
    Grid dragKeyLayout, desktopHeaderBand;
    DragKeyDockPreview dragKeyDockPreview;
    DragKeyPlacement dragKeyPreviewPlacement;
    double dragKeyPreviewPosition;
    bool manualDragKeyMove;
    bool changingDragKeyDock;
    bool hasFloatingDragKeyPosition;

    bool IsDragKeyDocked => dragKeyDockHost?.Child != null;
    DragKeySize ToolbarSize => secureMode ? DragKeySize.SignInNormal : DragKeySize.FromSetting(cfg.SmallDragKeyIcons);
    double SignInToolbarScale => DragKeySize.SignInScale(secureMode && cfg.SmallDragKeyIcons);
    double ToolbarDockScale => dragKeyScale * SignInToolbarScale;
    // Keep the stored keyboard frame independent of the toolbar's display size.
    double DockWidth(DragKeyPlacement placement)
        => placement is DragKeyPlacement.Left or DragKeyPlacement.Right ? DragKeySize.Normal.SideWidth * SignInToolbarScale + DragKeyDocking.Gap : 0;
    double DockHeight(DragKeyPlacement placement)
        => placement == DragKeyPlacement.Top ? DragKeySize.Normal.TopHeight * SignInToolbarScale + DragKeyDocking.Gap : 0;
    double SideDockHeight => secureMode ? ToolbarSize.TopHeight : ToolbarSize.SideHeight(cfg.RightDragEnabled, cfg.MiddleDragEnabled);

    UIElement BuildDesktopHeader(UIElement menu)
    {
        menuLayer.HorizontalAlignment = HorizontalAlignment.Left;
        menuLayer.VerticalAlignment = VerticalAlignment.Center;
        desktopHeaderBand = new Grid { Background = SoftKeyKeyDesign.WindowSurface, Children = { menu } };
        ApplyDesktopMenuLayout(titleVisible: true);
        menuLayer.SizeChanged += (_, e) => { if (e.WidthChanged) UpdateDragKeyHostLocations(); };
        desktopHeaderBand.SizeChanged += (_, e) => { if (e.WidthChanged) UpdateDragKeyHostLocations(); };
        return desktopHeaderBand;
    }

    UIElement BuildDragKeyLayout(FrameworkElement keyboard)
    {
        keyboardSurface = keyboard;
        var layout = dragKeyLayout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(keyboard, 1);
        Grid.SetColumn(keyboard, 1);
        layout.Children.Add(keyboard);
        dragKeyDockHost = new Border { Visibility = Visibility.Collapsed };
        layout.Children.Add(dragKeyDockHost);
        dragKeyDockPreview = new DragKeyDockPreview(secureMode);
        layout.Children.Add(dragKeyDockPreview);
        layout.SizeChanged += (_, _) => UpdateDragKeyHostLocations();
        return layout;
    }

    void UpdateDragKeyHostLocations()
    {
        if (IsDragKeyDocked) PlaceDragKeyHost(dragKeyDockHost, cfg.DragKeyPlacement, SavedDockPosition(cfg.DragKeyPlacement));
        if (dragKeyDockPreview?.IsVisible == true) PlaceDragKeyHost(dragKeyDockPreview, dragKeyPreviewPlacement, dragKeyPreviewPosition);
    }

    void ApplyDesktopMenuLayout(bool titleVisible)
    {
        desktopHeaderBand.Height = cfg.MenuAboveDragKey ? DesktopMenuRowHeight : double.NaN;
        desktopHeaderBand.Visibility = titleVisible || !cfg.MenuAboveDragKey ? Visibility.Visible : Visibility.Collapsed;
        UpdateDragKeyHostLocations();
    }

    double DesktopHeaderRowsHeight => desktopHeaderBand.ActualHeight + dragKeyLayout.RowDefinitions[0].ActualHeight;

    bool IsDesktopHeaderAt(Point screen)
        => IsPointInside(menuLayer, screen) || (IsDragKeyDocked && cfg.DragKeyPlacement == DragKeyPlacement.Top
            && dragKeyWin?.ContainsSurface(screen) == true);

    void PlaceDragKeyHost(FrameworkElement host, DragKeyPlacement placement, double position)
    {
        bool top = placement == DragKeyPlacement.Top;
        double offset = DragKeyDocking.Offset(placement, position, dragKeyLayout.RenderSize, ToolbarDockScale, ToolbarSize, SideDockHeight);
        double topLeft = DragKeyDocking.TopInset + offset;
        bool inHeader = false;
        if (top && !cfg.MenuAboveDragKey && desktopHeaderBand?.ActualWidth > 0)
        {
            double left = dragKeyLayout.TranslatePoint(default, desktopHeaderBand).X + topLeft;
            double menuEnd = menuLayer.ActualWidth + 12;
            double lastLeft = desktopHeaderBand.ActualWidth - DragKeyDocking.TopInset - ToolbarSize.HorizontalWidth * ToolbarDockScale;
            inHeader = lastLeft >= menuEnd;
            if (inHeader) topLeft = Math.Clamp(left, menuEnd, lastLeft);
        }
        var parent = inHeader ? desktopHeaderBand : dragKeyLayout;
        if (host.Parent is Grid current && current != parent)
        {
            current.Children.Remove(host);
            parent.Children.Add(host);
        }

        Grid.SetColumn(host, top || placement == DragKeyPlacement.Left ? 0 : 2);
        Grid.SetRow(host, top ? 0 : 1);
        Grid.SetColumnSpan(host, top && !inHeader ? 3 : 1);
        host.Margin = top ? new Thickness(topLeft, 2, DragKeyDocking.TopInset, 2)
            : placement == DragKeyPlacement.Left ? new Thickness(0, offset, 4, 0) : new Thickness(4, offset, 0, 0);
        host.HorizontalAlignment = HorizontalAlignment.Left;
        host.VerticalAlignment = inHeader ? VerticalAlignment.Center : VerticalAlignment.Top;
    }

    void UpdateDragKeyDockPreview(Point screen)
    {
        Rect bounds;
        if (mouseActions.IsWindowDragging && draggedWindow == dragKeyWin)
        {
            int x = windowDragBounds.Left + (int)screen.X - windowDragPointer.X;
            int y = windowDragBounds.Top + (int)screen.Y - windowDragPointer.Y;
            var bounded = DragKeyScreenBounds.ConstrainToWorkArea(new Native.RECT { Left = x, Top = y,
                Right = x + windowDragBounds.Right - windowDragBounds.Left, Bottom = y + windowDragBounds.Bottom - windowDragBounds.Top });
            bounds = new Rect(bounded.Left, bounded.Top, bounded.Right - bounded.Left, bounded.Bottom - bounded.Top);
        }
        else if (dragKeyWin?.IsVisible == true && Native.GetWindowRect(new WindowInteropHelper(dragKeyWin).Handle, out var rect))
            bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        else { ClearDragKeyDockPreview(); return; }
        PreviewDragKeyBounds(bounds);
    }

    void PreviewDragKeyBounds(Rect screenBounds)
    {
        bool moving = manualDragKeyMove || (mouseActions.IsWindowDragging && draggedWindow == dragKeyWin);
        var match = moving && !IsDragKeyDocked && !mainWindowActionsLocked
            ? DockMatchFor(screenBounds) : new DragKeyDockMatch(DragKeyPlacement.Floating, 0);
        var placement = match.Placement;
        if (placement == dragKeyPreviewPlacement && Math.Abs(match.Position - dragKeyPreviewPosition) < 0.000001) return;
        dragKeyPreviewPlacement = placement;
        dragKeyPreviewPosition = match.Position;
        if (placement == DragKeyPlacement.Floating)
        {
            dragKeyDockPreview.Visibility = Visibility.Collapsed;
            return;
        }
        PlaceDragKeyHost(dragKeyDockPreview, placement, match.Position);
        dragKeyDockPreview.SetPlacement(placement);
        dragKeyDockPreview.SetDockScale(ToolbarDockScale);
        dragKeyDockPreview.Visibility = Visibility.Visible;
    }

    void ClearDragKeyDockPreview()
    {
        dragKeyPreviewPlacement = DragKeyPlacement.Floating;
        dragKeyPreviewPosition = 0;
        if (dragKeyDockPreview != null) dragKeyDockPreview.Visibility = Visibility.Collapsed;
    }

    void SetDragKeyPlacement(DragKeyPlacement placement)
        => SetDragKeyPlacementAt(placement, SavedDockPosition(placement));

    double SavedDockPosition(DragKeyPlacement placement) => placement switch
    {
        DragKeyPlacement.Top => cfg.DragKeyTopPosition,
        DragKeyPlacement.Left => cfg.DragKeyLeftPosition,
        DragKeyPlacement.Right => cfg.DragKeyRightPosition,
        _ => 0
    };

    void SetDragKeyPlacementAt(DragKeyPlacement placement, double position)
    {
        if (desktopSessionSuspended || !Enum.IsDefined(placement) || mainWindowActionsLocked || changingDragKeyDock) return;
        var returnPosition = dragKeyMoveStartPosition;
        if (!CancelMouseAction()) return;
        CloseMenu();
        var previous = cfg.DragKeyPlacement;
        CaptureWindowPositions();
        if (previous == DragKeyPlacement.Floating && placement != DragKeyPlacement.Floating && returnPosition != null)
            cfg.DragKeyPosition = returnPosition;
        position = double.IsFinite(position) ? Math.Clamp(position, 0, 1) : 0;
        if (previous == placement && SavedDockPosition(placement) == position) { UpdateDragKeyPresentation(); return; }
        changingDragKeyDock = true;
        try
        {
            cfg.DragKeyPlacement = placement;
            if (secureMode) UpdateKeyboardMinimumSize();
            if (previous != DragKeyPlacement.Floating && placement == DragKeyPlacement.Floating)
                hasFloatingDragKeyPosition = false; // Restore the saved pre-drag position when undocking.
            if (placement == DragKeyPlacement.Top) cfg.DragKeyTopPosition = position;
            else if (placement == DragKeyPlacement.Left) cfg.DragKeyLeftPosition = position;
            else if (placement == DragKeyPlacement.Right) cfg.DragKeyRightPosition = position;
            // Reflow inside the existing frame, matching the live preview without a resize or jump.
            UpdateDragKeyPresentation();
            cfg.WindowWidth = Width - DockWidth(placement);
            cfg.WindowHeight = Height + hiddenHeaderHeight - DockHeight(placement);
        }
        finally { changingDragKeyDock = false; }
        SaveSettings();
        SuppressCurrentDwell();
    }

    void UpdateDragKeyPresentation()
    {
        if (desktopSessionSuspended || dragKeyWin == null || dragKeyDockHost == null) return;
        ClearDragKeyDockPreview();
        bool docked = !keyboardHidden && cfg.DragKeyPlacement != DragKeyPlacement.Floating && WindowState != WindowState.Minimized;
        // One shared visual tree: buttons are reparented, never duplicated or recreated.
        dragKeyDockHost.Child = null;
        dragKeyWin.Content = null;
        dragKeyDockHost.Visibility = Visibility.Collapsed;
        dragKeyWin.SetDockLayout(docked, docked && cfg.DragKeyPlacement != DragKeyPlacement.Top);
        dragKeyWin.SetDockScale(dragKeyScale);
        if (docked)
        {
            if (dragKeyWin.IsVisible)
            {
                hasFloatingDragKeyPosition = true;
                dragKeyWin.Hide();
            }
            PlaceDragKeyHost(dragKeyDockHost, cfg.DragKeyPlacement, SavedDockPosition(cfg.DragKeyPlacement));
            dragKeyDockHost.Child = dragKeyWin.Surface;
            dragKeyDockHost.Visibility = Visibility.Visible;
        }
        else
        {
            dragKeyWin.Content = dragKeyWin.Surface;
            dragKeyWin.Show();
            if (!hasFloatingDragKeyPosition)
            {
                if (!WindowPositioning.Restore(new WindowInteropHelper(dragKeyWin).Handle, cfg.DragKeyPosition))
                {
                    var area = SystemParameters.WorkArea;
                    dragKeyWin.Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - dragKeyWin.Width));
                    dragKeyWin.Top = Math.Max(area.Top, Top - dragKeyWin.Height - 8);
                }
                hasFloatingDragKeyPosition = true;
            }
            KeepDragKeyAboveKeyboard();
        }
        RefreshDragKey();
    }

    void FloatDragKey(Point screen)
    {
        if (!IsDragKeyDocked) return;
        Point local = dragKeyWin.Surface.PointFromScreen(screen);
        bool vertical = cfg.DragKeyPlacement is DragKeyPlacement.Left or DragKeyPlacement.Right;
        var size = ToolbarSize;
        double anchorY = vertical && local.Y >= size.TitleHeight ? size.TitleHeight / 2
            : Math.Clamp(local.Y, 1, size.FloatingHeight - 1);
        // Preserve the grab point when the gently scaled dock returns to its unscaled floating form.
        var toDevice = PresentationSource.FromVisual(dragKeyWin.Surface).CompositionTarget.TransformToDevice;
        Vector anchor = toDevice.Transform(new Vector(Math.Clamp(local.X, 1, size.HorizontalWidth - 1), anchorY) * SignInToolbarScale);
        Point origin = new(screen.X - anchor.X, screen.Y - anchor.Y);
        SetDragKeyPlacement(DragKeyPlacement.Floating);
        // Keep the grabbed title in place during reparenting, including scaled displays.
        Native.MoveWithoutActivation(new WindowInteropHelper(dragKeyWin).Handle, (int)origin.X, (int)origin.Y);
        hasFloatingDragKeyPosition = true;
    }

    bool BeginDragKeyMove(Native.POINT point)
    {
        if (!cfg.AutoClick || mainWindowActionsLocked || mouseActions.IsDragging
            || !mouseActions.IsDragSelected) return false;
        var action = mouseActions.Selected;
        var returnPosition = IsDragKeyDocked ? cfg.DragKeyPosition : null;
        FloatDragKey(new Point(point.X, point.Y));
        if (mouseActions.Selected != action) mouseActions.Select(action);
        bool started = BeginWindowDrag(dragKeyWin, point);
        if (started && returnPosition != null) dragKeyMoveStartPosition = returnPosition;
        return started;
    }

    void MoveDragKeyWithMouse(Point screen)
    {
        if (mainWindowActionsLocked || !CancelMouseAction()) return;
        CloseMenu();
        var returnPosition = IsDragKeyDocked ? cfg.DragKeyPosition
            : WindowPositioning.Capture(new WindowInteropHelper(dragKeyWin).Handle);
        FloatDragKey(screen);
        dragKeyMoveStartPosition = returnPosition;
        manualDragKeyMove = true;
        try
        {
            UpdateDragKeyDockPreview(screen);
            dragKeyWin.DragMove();
            TryDockDragKey();
        }
        finally { manualDragKeyMove = false; dragKeyMoveStartPosition = null; ClearDragKeyDockPreview(); }
        SaveSettings();
        SuppressCurrentDwell();
    }

    void FloatDragKeyFromBackground(Point screen)
    {
        if (!IsDragKeyDocked || mainWindowActionsLocked || !CancelMouseAction()) return;
        CloseMenu();
        SetDragKeyPlacement(DragKeyPlacement.Floating);
        SuppressCurrentDwell();
    }

    bool FinishWindowDrag()
    {
        bool moveToolbar = mouseActions.IsWindowDragging && draggedWindow == dragKeyWin;
        bool movedWindow = mouseActions.IsWindowDragging;
        var returnPosition = dragKeyMoveStartPosition;
        var placement = dragKeyPreviewPlacement;
        double position = dragKeyPreviewPosition;
        bool released = CancelMouseAction();
        if (released && moveToolbar && placement != DragKeyPlacement.Floating)
        {
            dragKeyMoveStartPosition = returnPosition;
            try { SetDragKeyPlacementAt(placement, position); }
            finally { dragKeyMoveStartPosition = null; }
        }
        if (released && movedWindow) SaveSettings();
        return released;
    }

    DragKeyDockMatch DockMatchFor(Rect screenBounds)
    {
        if (WindowState != WindowState.Normal || !IsVisible) return new(DragKeyPlacement.Floating, 0);
        Point start = dragKeyLayout.PointFromScreen(screenBounds.TopLeft);
        Point end = dragKeyLayout.PointFromScreen(screenBounds.BottomRight);
        double headerHeight = desktopHeaderBand?.IsVisible == true
            ? desktopHeaderBand.ActualHeight + headerSpacing.ActualHeight : 0;
        return DragKeyDocking.Find(new Rect(start, end), dragKeyLayout.RenderSize, dragKeyPreviewPlacement,
            ToolbarDockScale, ToolbarSize, SideDockHeight, headerHeight);
    }

    void TryDockDragKey()
    {
        if (IsDragKeyDocked || !Native.GetWindowRect(new WindowInteropHelper(dragKeyWin).Handle, out var bounds)) return;
        PreviewDragKeyBounds(new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        var placement = dragKeyPreviewPlacement;
        double position = dragKeyPreviewPosition;
        ClearDragKeyDockPreview();
        if (placement != DragKeyPlacement.Floating) SetDragKeyPlacementAt(placement, position);
    }
}
