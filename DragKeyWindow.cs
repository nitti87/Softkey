using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;

// The toolbar only displays state and raises actions. KeyboardWindow owns all click behaviour.
sealed class DragKeyWindow : Window
{
    public const string DisplayName = "Dragkey";
    public static readonly CornerRadius PanelCornerRadius = new(8);
    public const string LeftDragLabel = "Vänsterdrag", RightDragLabel = "Högerdrag", MiddleDragLabel = "Mittenknappsdrag";
    const string UndockLabel = "Avdocka " + DisplayName;
    const string PowerLabel = "Stäng SoftKey";
    const string KeyboardToTopLabel = "Flytta tangentbordet till överkanten";
    const string KeyboardToBottomLabel = "Flytta tangentbordet till nederkanten";
    readonly DragKeyButton autoClick;
    readonly DragKeyButton rightClick = new("Högerklick", DragKeySymbol.RightClick),
        doubleClick = new("Dubbelklick", DragKeySymbol.DoubleClick),
        drag = new(LeftDragLabel, DragKeySymbol.LeftDrag),
        rightDrag = new(RightDragLabel, DragKeySymbol.RightDrag) { Visibility = Visibility.Collapsed },
        middleDrag = new(MiddleDragLabel, DragKeySymbol.MiddleDrag) { Visibility = Visibility.Collapsed },
        keyboardToggle = new("Dölj tangentbordet", DragKeySymbol.Keyboard),
        headerToggle = new("Dölj titelrad", DragKeySymbol.WindowHeader),
        moveKeyboardEdge = new(KeyboardToTopLabel, DragKeySymbol.KeyboardToTop) { ToolTip = KeyboardToTopLabel },
        undock = new(UndockLabel, DragKeySymbol.Undock) { Visibility = Visibility.Collapsed },
        power = new(PowerLabel, DragKeySymbol.Power) { ToolTip = PowerLabel };
    readonly System.Windows.Shapes.Ellipse stateDot = new()
    {
        Width = 5, Height = 5, Margin = new Thickness(0, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    readonly TextBlock hiddenKeyboardStatus = new()
    {
        FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = SoftKeyKeyDesign.SlateAccentText,
        Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed
    };
    bool allowClose;
    readonly bool autoClickOnly;
    bool compactSignIn;
    double dockScale = 1;
    readonly Border title;
    readonly UniformGrid buttons;
    readonly UniformGrid grip;
    readonly Grid body;
    DragKeySize size = DragKeySize.Normal;
    bool isDocked;
    bool dockedVertically;
    bool keyboardMovesToTop = true;
    string KeyboardMoveLabel => keyboardMovesToTop ? KeyboardToTopLabel : KeyboardToBottomLabel;
    Point? backgroundPress;
    public FrameworkElement Surface { get; }
    public string StatusText { get; private set; }
    public Action<Point> MoveTitle { get; set; }
    public Action<Rect> MovingBounds { get; set; }
    public Action<Point> FloatFromBackground { get; set; }

    public DragKeyWindow(Action toggleAutoClick, Action toggleRightClick, Action toggleDoubleClick, Action toggleDrag,
        bool autoClickOnly = false, Action toggleKeyboard = null, Action toggleHeader = null, Action toggleMiddleDrag = null,
        Action toggleRightDrag = null, Action requestClose = null, Action moveKeyboard = null)
    {
        this.autoClickOnly = autoClickOnly;
        if (autoClickOnly) size = DragKeySize.SignInNormal;
        autoClick = new DragKeyButton("AutoClick", DragKeySymbol.AutoClick, signIn: autoClickOnly);
        Title = DisplayName;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = size.HorizontalWidth;
        Height = size.FloatingHeight;
        SizeToContent = SizeToContent.Manual;
        ShowInTaskbar = ShowActivated = Focusable = false;
        FontFamily = new FontFamily("Segoe UI");
        Foreground = SoftKeyKeyDesign.PrimaryText;
        Background = SoftKeyKeyDesign.WindowSurface;
        UseLayoutRounding = SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var titleContent = new Grid();
        titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleLabel = new DockPanel();
        titleLabel.Children.Add(stateDot);
        titleLabel.Children.Add(new TextBlock
        {
            Text = DisplayName, FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        titleLabel.Children.Add(hiddenKeyboardStatus);
        titleContent.Children.Add(titleLabel);
        grip = new UniformGrid { Columns = 2, Rows = 3, Width = 8, Height = 10, Margin = new Thickness(12, 0, 0, 0) };
        for (int i = 0; i < 6; i++) grip.Children.Add(new System.Windows.Shapes.Ellipse
            { Width = 2, Height = 2, Fill = Brush(0x9A, 0x95, 0x8C) });
        Grid.SetColumn(grip, 1);
        titleContent.Children.Add(grip);
        title = new Border
        {
            Height = size.TitleHeight, CornerRadius = new CornerRadius(7, 7, 0, 0),
            Background = SoftKeyKeyDesign.TraySurface, Padding = new Thickness(6, 2, 6, 2),
            Cursor = Cursors.SizeAll, Child = titleContent
        };
        buttons = new UniformGrid { Columns = autoClickOnly ? 1 : 0, Rows = 1, Margin = size.PanelMargin };
        buttons.Children.Add(autoClick);
        if (!autoClickOnly)
        {
            buttons.Children.Add(rightClick);
            buttons.Children.Add(doubleClick);
            buttons.Children.Add(drag);
            buttons.Children.Add(rightDrag);
            buttons.Children.Add(middleDrag);
            buttons.Children.Add(keyboardToggle);
            buttons.Children.Add(headerToggle);
            buttons.Children.Add(moveKeyboardEdge);
            buttons.Children.Add(undock);
            buttons.Children.Add(power);
        }
        body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(size.TitleHeight) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(title);
        Grid.SetRow(buttons, 1);
        body.Children.Add(buttons);
        Surface = new Border
        {
            Cursor = Cursors.SizeAll, CornerRadius = PanelCornerRadius,
            Background = SoftKeyKeyDesign.WindowSurface,
            BorderBrush = SoftKeyKeyDesign.Outline, BorderThickness = new Thickness(1), Child = body
        };
        Content = Surface;
        Surface.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!BeginBackgroundPress(e.OriginalSource as DependencyObject,
                Surface.PointToScreen(e.GetPosition(Surface)), e.ClickCount)) return;
            if (backgroundPress.HasValue) Mouse.Capture(Surface, CaptureMode.SubTree);
            e.Handled = true;
        };
        Surface.PreviewMouseMove += (_, e) =>
        {
            if (backgroundPress.HasValue && ContinueBackgroundPress(Surface.PointToScreen(e.GetPosition(Surface)),
                e.LeftButton == MouseButtonState.Pressed)) e.Handled = true;
        };
        Surface.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!backgroundPress.HasValue) return;
            ClearBackgroundPress();
            e.Handled = true;
        };
        Surface.LostMouseCapture += (_, _) => backgroundPress = null;
        autoClick.Click += (_, _) => toggleAutoClick();
        rightClick.Click += (_, _) => toggleRightClick();
        doubleClick.Click += (_, _) => toggleDoubleClick();
        drag.Click += (_, _) => toggleDrag();
        rightDrag.Click += (_, _) => toggleRightDrag?.Invoke();
        middleDrag.Click += (_, _) => toggleMiddleDrag?.Invoke();
        keyboardToggle.Click += (_, _) => toggleKeyboard?.Invoke();
        headerToggle.Click += (_, _) => toggleHeader?.Invoke();
        moveKeyboardEdge.Click += (_, _) => moveKeyboard?.Invoke();
        power.Click += (_, _) => requestClose?.Invoke();
        undock.Click += (_, _) => FloatFromBackground?.Invoke(undock.PointToScreen(new Point(undock.ActualWidth / 2, undock.ActualHeight / 2)));
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.NoActivate(handle);
            HwndSource.FromHwnd(handle).AddHook(FilterWindowMessages);
        };
        Loaded += (_, _) => ApplyScreenBounds();
        // Keep the resume button available until SoftKey itself exits.
        Closing += (_, e) => e.Cancel = !allowClose;
    }

    [StructLayout(LayoutKind.Sequential)] struct WindowPositionChange
    {
        public IntPtr Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    IntPtr FilterWindowMessages(IntPtr window, int message, IntPtr wParam, IntPtr data, ref bool handled)
    {
        if (!isDocked && data != IntPtr.Zero)
        {
            if (message == 0x0216) // WM_MOVING: constrain the actual mouse-drag outline and docking preview.
            {
                var bounds = DragKeyScreenBounds.ConstrainToWorkArea(Marshal.PtrToStructure<Native.RECT>(data));
                Marshal.StructureToPtr(bounds, data, false);
                MovingBounds?.Invoke(new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
                handled = true;
                return new IntPtr(1);
            }
            if (message == 0x0046 && Native.GetWindowRect(window, out var current)) // WM_WINDOWPOSCHANGING
            {
                var change = Marshal.PtrToStructure<WindowPositionChange>(data);
                const uint NoSize = 1, NoMove = 2;
                if ((change.Flags & (NoSize | NoMove)) != (NoSize | NoMove))
                {
                    int x = (change.Flags & NoMove) != 0 ? current.Left : change.X;
                    int y = (change.Flags & NoMove) != 0 ? current.Top : change.Y;
                    int width = (change.Flags & NoSize) != 0 ? current.Right - current.Left : change.Width;
                    int height = (change.Flags & NoSize) != 0 ? current.Bottom - current.Top : change.Height;
                    if (width > 0 && height > 0)
                    {
                        var bounds = DragKeyScreenBounds.ConstrainToWorkArea(new Native.RECT { Left = x, Top = y,
                            Right = (int)Math.Min(int.MaxValue, (long)x + width), Bottom = (int)Math.Min(int.MaxValue, (long)y + height) });
                        if (bounds.Left != x || bounds.Top != y)
                        {
                            change.X = bounds.Left; change.Y = bounds.Top; change.Flags &= ~NoMove;
                        }
                        if (bounds.Right - bounds.Left != width || bounds.Bottom - bounds.Top != height)
                        {
                            change.Width = bounds.Right - bounds.Left; change.Height = bounds.Bottom - bounds.Top; change.Flags &= ~NoSize;
                        }
                        Marshal.StructureToPtr(change, data, false);
                    }
                }
            }
        }
        if (message is 0x007E or 0x001A) // Display/work-area changes can make the previous position unreachable.
            Dispatcher.BeginInvoke(new Action(ApplyScreenBounds));
        if (message != Native.WmMouseActivate) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MaNoActivate);
    }

    void ApplyScreenBounds()
    {
        if (isDocked || !IsVisible) return;
        var window = new WindowInteropHelper(this).Handle;
        if (!Native.GetWindowRect(window, out var current)) return;
        var bounds = DragKeyScreenBounds.ConstrainToWorkArea(current);
        if (current.Left != bounds.Left || current.Top != bounds.Top || current.Right != bounds.Right || current.Bottom != bounds.Bottom)
            Native.ResizeWithoutActivation(window, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    public void Refresh(bool enabled, MouseAction selected, bool dragging, bool actionsAvailable, string movingWindow = null,
        bool resizing = false, bool keyboardHidden = false, bool headerHidden = false)
    {
        System.Windows.Automation.AutomationProperties.SetItemStatus(autoClick, enabled ? "På" : "Av");
        autoClick.ChangeSymbol(enabled ? DragKeySymbol.AutoClick : DragKeySymbol.AutoClickPaused);
        autoClick.Foreground = SoftKeyKeyDesign.SlateAccentText;
        autoClick.Background = enabled ? autoClick.RestingFace : SoftKeyKeyDesign.LetterFace;
        autoClick.BorderBrush = enabled ? SoftKeyKeyDesign.Outline : Brush(0xC6, 0xC2, 0xB8);
        bool rightArmed = selected == MouseAction.Right;
        bool doubleArmed = selected == MouseAction.Double;
        bool dragArmed = MouseActionState.IsDragAction(selected);
        SetAction(rightClick, "Högerklick", rightArmed, enabled && actionsAvailable && !dragging);
        SetAction(doubleClick, "Dubbelklick", doubleArmed, enabled && actionsAvailable && !dragging);
        RefreshDragButton(drag, LeftDragLabel, selected == MouseAction.Drag, dragging, enabled, actionsAvailable);
        RefreshDragButton(rightDrag, RightDragLabel, selected == MouseAction.RightDrag, dragging, enabled, actionsAvailable);
        RefreshDragButton(middleDrag, MiddleDragLabel, selected == MouseAction.MiddleDrag, dragging, enabled, actionsAvailable);
        SetAction(undock, UndockLabel, false, enabled && isDocked && actionsAvailable && !dragging);
        SetAction(power, PowerLabel, false, enabled && !autoClickOnly);
        string keyboardLabel = keyboardHidden ? "Visa tangentbordet" : "Dölj tangentbordet";
        SetAction(keyboardToggle, keyboardLabel, keyboardHidden, enabled && actionsAvailable && !dragging);
        System.Windows.Automation.AutomationProperties.SetName(keyboardToggle, keyboardLabel);
        System.Windows.Automation.AutomationProperties.SetItemStatus(keyboardToggle,
            !keyboardToggle.IsEnabled ? "Pausad" : keyboardHidden ? "Tangentbordet dolt" : "Tangentbordet synligt");
        System.Windows.Automation.AutomationProperties.SetHelpText(keyboardToggle, $"{keyboardLabel}. {DisplayName} finns kvar.");
        string headerLabel = headerHidden ? "Visa titelrad" : "Dölj titelrad";
        SetAction(headerToggle, headerLabel, headerHidden, enabled && actionsAvailable && !dragging && !keyboardHidden);
        SetAction(moveKeyboardEdge, KeyboardMoveLabel, false, enabled && actionsAvailable && !dragging && !keyboardHidden);
        headerToggle.ToolTip = headerLabel;
        System.Windows.Automation.AutomationProperties.SetName(headerToggle, headerLabel);
        System.Windows.Automation.AutomationProperties.SetItemStatus(headerToggle,
            !headerToggle.IsEnabled ? "Pausad" : headerHidden ? "Titelraden är dold" : "Titelraden visas");
        string description = resizing ? "Ändrar SoftKeys storlek. Vila med pekaren för att avsluta."
            : movingWindow != null ? "Flyttar " + movingWindow + ". Vila med pekaren för att släppa."
            : dragging ? "Flytta pekaren och vila för att släppa."
            : !enabled ? "Autoklick pausat. Vila på AutoClick för att fortsätta."
            : !actionsAvailable ? "Musval pausade medan en dialog är öppen."
            : dragArmed ? "Vila på ett objekt eller en namnlist för att flytta."
            : doubleArmed ? "Dubbelklick valt. Vila på målet för att klicka."
            : rightArmed ? "Högerklick valt. Vila på målet för att klicka."
            : "Vila med pekaren för att vänsterklicka.";
        System.Windows.Automation.AutomationProperties.SetHelpText(this, description);
        System.Windows.Automation.AutomationProperties.SetHelpText(Surface, description);
        StatusText = autoClickOnly ? null : !enabled ? "Autoklick pausat" : resizing ? "Ändrar storlek"
            : movingWindow != null ? "Flyttar · vila för att släppa" : dragging ? "Drag pågår · vila för att släppa"
            : !actionsAvailable ? null : selected switch
            {
                MouseAction.Right => "Högerklick valt",
                MouseAction.Double => "Dubbelklick valt",
                MouseAction.Drag => LeftDragLabel + " valt",
                MouseAction.RightDrag => RightDragLabel + " valt",
                MouseAction.MiddleDrag => MiddleDragLabel + " valt",
                _ => null
            };
        hiddenKeyboardStatus.Text = StatusText ?? "";
        hiddenKeyboardStatus.Visibility = keyboardHidden && StatusText != null ? Visibility.Visible : Visibility.Collapsed;
        stateDot.Fill = !enabled ? Brush(0x94, 0x8F, 0x86)
            : dragging ? Brush(0x9C, 0x5D, 0x2D)
            : selected != MouseAction.Left ? Brush(0xA1, 0x7C, 0x32) : SoftKeyKeyDesign.SlateAccent;
        System.Windows.Automation.AutomationProperties.SetName(autoClick, enabled ? "AutoClick på" : "AutoClick av");
        System.Windows.Automation.AutomationProperties.SetName(rightClick, rightArmed && rightClick.IsEnabled ? "Högerklick valt – välj mål" : "Högerklick");
    }

    public void SetKeyboardMoveDestination(bool toTop)
    {
        if (keyboardMovesToTop == toTop) return;
        keyboardMovesToTop = toTop;
        moveKeyboardEdge.ChangeSymbol(toTop ? DragKeySymbol.KeyboardToTop : DragKeySymbol.KeyboardToBottom);
        moveKeyboardEdge.ToolTip = KeyboardMoveLabel;
        System.Windows.Automation.AutomationProperties.SetName(moveKeyboardEdge, KeyboardMoveLabel);
    }

    public DwellTarget TargetAt(Point screen)
        => IsPowerAt(screen) ? DwellTarget.CloseSoftKey
            : undock.IsEnabled && Contains(undock, screen) ? DwellTarget.UndockDragKey
            : keyboardToggle.IsEnabled && Contains(keyboardToggle, screen) ? DwellTarget.ToggleKeyboard
            : headerToggle.IsEnabled && Contains(headerToggle, screen) ? DwellTarget.ToggleKeyboardHeader
            : moveKeyboardEdge.IsEnabled && Contains(moveKeyboardEdge, screen) ? DwellTarget.MoveKeyboardEdge
            : Contains(autoClick, screen) ? DwellTarget.AutoClick
            : rightClick.IsEnabled && Contains(rightClick, screen) ? DwellTarget.RightClick
            : doubleClick.IsEnabled && Contains(doubleClick, screen) ? DwellTarget.DoubleClick
            : drag.IsEnabled && Contains(drag, screen) ? DwellTarget.Drag
            : rightDrag.IsEnabled && Contains(rightDrag, screen) ? DwellTarget.RightDrag
            : middleDrag.IsEnabled && Contains(middleDrag, screen) ? DwellTarget.MiddleDrag : DwellTarget.None;

    public bool IsTitleAt(Point screen) => Contains(title, screen);

    public bool IsPowerAt(Point screen) => power.IsEnabled && Contains(power, screen);

    public bool ContainsSurface(Point screen) => Contains(Surface, screen);

    public bool IsMoveAreaAt(Point screen) => ContainsSurface(screen)
        && !Contains(autoClick, screen) && !Contains(rightClick, screen)
        && !Contains(doubleClick, screen) && !Contains(drag, screen) && !Contains(rightDrag, screen)
        && !Contains(middleDrag, screen) && !Contains(keyboardToggle, screen)
        && !Contains(headerToggle, screen) && !Contains(moveKeyboardEdge, screen)
        && !Contains(undock, screen) && !Contains(power, screen);

    bool BeginBackgroundPress(DependencyObject source, Point screen, int clicks)
    {
        if (!IsBackgroundSource(source) || !IsMoveAreaAt(screen)) return false;
        ClearBackgroundPress();
        if (clicks == 2) FloatFromBackground?.Invoke(screen);
        else if (clicks == 1) backgroundPress = screen;
        return true;
    }

    bool ContinueBackgroundPress(Point screen, bool leftDown)
    {
        if (!backgroundPress.HasValue) return false;
        if (!leftDown) { ClearBackgroundPress(); return false; }
        Point start = backgroundPress.Value;
        // Mouse coordinates are physical pixels; use WPF's device-independent drag threshold.
        var scale = PresentationSource.FromVisual(Surface)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Vector distance = scale.Transform(screen - start);
        if (System.Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance
            && System.Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return false;
        ClearBackgroundPress();
        if (MoveTitle != null) MoveTitle(screen);
        else DragMove();
        return true;
    }

    bool IsBackgroundSource(DependencyObject source)
    {
        for (var current = source; current != null;)
        {
            if (current is ButtonBase) return false;
            if (current == Surface) return true;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    void ClearBackgroundPress()
    {
        backgroundPress = null;
        if (Mouse.Captured == Surface) Mouse.Capture(null);
    }

    public void SetDockLayout(bool docked, bool vertical)
    {
        isDocked = docked;
        title.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        title.Height = size.TitleHeight;
        body.RowDefinitions[0].Height = new GridLength(docked ? 0 : size.TitleHeight);
        undock.Visibility = docked && !autoClickOnly ? Visibility.Visible : Visibility.Collapsed;
        dockedVertically = vertical && !autoClickOnly;
        buttons.Columns = autoClickOnly ? 1 : dockedVertically ? DragKeyDocking.SideColumns : 0;
        buttons.Rows = dockedVertically ? 0 : 1;
        buttons.Margin = size.PanelMargin;
        FitButtons();
        grip.Margin = new Thickness(vertical ? 2 : 12, 0, 0, 0);
        Surface.Width = docked ? dockedVertically ? size.SideWidth : size.HorizontalWidth : double.NaN;
        Surface.Height = docked ? dockedVertically ? SidePanelHeight : size.TopHeight : double.NaN;
        UpdateSurfaceScale();
    }

    public void SetDragOptions(bool right, bool middle)
    {
        if (autoClickOnly) return;
        rightDrag.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
        middleDrag.Visibility = middle ? Visibility.Visible : Visibility.Collapsed;
        if (dockedVertically) Surface.Height = SidePanelHeight;
    }

    public void SetSmallSize(bool small)
    {
        if (autoClickOnly)
        {
            if (compactSignIn == small) return;
            compactSignIn = small;
            double scale = DragKeySize.SignInScale(small);
            Width = size.HorizontalWidth * scale;
            Height = size.FloatingHeight * scale;
            UpdateSurfaceScale();
            return;
        }
        var newSize = DragKeySize.FromSetting(small);
        if (size == newSize) return;
        size = newSize;
        foreach (DragKeyButton button in buttons.Children)
            DragKeyButton.SetIconSize((Viewbox)button.Content, small);
        Width = size.HorizontalWidth;
        Height = size.FloatingHeight;
        SetDockLayout(isDocked, dockedVertically);
    }

    double SidePanelHeight => size.SideHeight(rightDrag.Visibility == Visibility.Visible,
        middleDrag.Visibility == Visibility.Visible);

    void FitButtons()
    {
        foreach (DragKeyButton button in buttons.Children)
            button.FitTarget(size, dockedVertically);
    }

    static void RefreshDragButton(DragKeyButton button, string label, bool selected, bool dragging, bool enabled, bool actionsAvailable)
    {
        bool held = selected && dragging && enabled;
        SetAction(button, label, selected, enabled && (dragging ? held : actionsAvailable));
        button.ToolTip = label;
        if (!held) return;
        button.Background = Brush(0xEE, 0xD0, 0xB5);
        button.BorderBrush = Brush(0x96, 0x59, 0x2C);
        System.Windows.Automation.AutomationProperties.SetName(button, $"{label} pågår – släpp");
        System.Windows.Automation.AutomationProperties.SetItemStatus(button, "Drag pågår");
    }

    public void SetDockScale(double scale)
    {
        dockScale = scale;
        UpdateSurfaceScale();
    }

    void UpdateSurfaceScale()
    {
        double effective = (isDocked ? dockScale : 1) * DragKeySize.SignInScale(compactSignIn);
        if (Surface.LayoutTransform.Value.M11 == effective) return;
        Surface.LayoutTransform = effective == 1 ? Transform.Identity : new ScaleTransform(effective, effective);
        foreach (DragKeyButton button in buttons.Children)
            ((Viewbox)button.Content).Child.InvalidateVisual();
    }

    static void SetAction(DragKeyButton button, string label, bool selected, bool enabled)
    {
        selected &= enabled;
        System.Windows.Automation.AutomationProperties.SetItemStatus(button,
            !enabled ? "Pausad" : selected ? "Valt - välj mål" : "Redo");
        button.Background = !enabled ? Brush(0xE4, 0xE1, 0xDA) : selected ? Brush(0xF2, 0xE5, 0xC4) : button.RestingFace;
        button.BorderBrush = !enabled ? Brush(0xCC, 0xC8, 0xBE) : selected ? Brush(0x94, 0x74, 0x35) : SoftKeyKeyDesign.Outline;
        button.Foreground = enabled ? SoftKeyKeyDesign.PrimaryText : Brush(0x8C, 0x88, 0x80);
        button.IsEnabled = enabled;
        System.Windows.Automation.AutomationProperties.SetName(button, selected ? label + " valt – välj mål" : label);
    }

    public void CloseWithApplication() { allowClose = true; Close(); }

    static bool Contains(FrameworkElement element, Point screen)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) == null) return false;
        Point local = element.PointFromScreen(screen);
        return local.X >= 0 && local.Y >= 0 && local.X < element.ActualWidth && local.Y < element.ActualHeight;
    }

    static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

}
