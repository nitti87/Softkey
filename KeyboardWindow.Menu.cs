using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using static SoftKeyPopupDesign;

// Menu bar, dropdown actions, and outside-click dismissal for KeyboardWindow.
partial class KeyboardWindow
{
    const int WordMenuIndex = 4;
    const int KeyboardMenuIndex = 1;

    static readonly Brush MenuStroke = SoftKeyKeyDesign.Outline;
    static readonly Brush MenuPanelStroke = new SolidColorBrush(Color.FromRgb(0xD2, 0xCF, 0xC6));
    static readonly Brush MenuSeparatorBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE3, 0xDC));
    static readonly Brush MenuHeaderHover = SoftKeyKeyDesign.HeaderHover;
    static readonly Brush ActiveMenu = SoftKeyKeyDesign.KeyHeld;
    static readonly Brush ActiveMenuBorder = SoftKeyKeyDesign.SlateOutline;
    static readonly Brush ActiveMenuText = SoftKeyKeyDesign.SlateAccentText;
    static readonly ControlTemplate MenuHeaderTemplate = KeyButtonTemplate(cornerRadius: 4);
    static readonly ControlTemplate MenuActionTemplate = KeyButtonTemplate(HorizontalAlignment.Stretch, cornerRadius: 4);

    readonly Button[] menuHeaders = new Button[4];
    readonly List<Control> menuTextControls = new();
    double menuFontSize = 13;
    StackPanel menuLayer;
    Border wordMenuAnchor;
    Border menuPanel;
    Border updateIndicator;
    Border updateMenuBadge;
    Button updateMenuAction;
    Popup menuPopup;
    int openMenu = -1;
    IntPtr outsideClickHook;
    LowLevelMouseProc outsideClickProc;

    void ScaleMenuText(double keyboardFontSize)
    {
        menuFontSize = Math.Clamp(keyboardFontSize * 0.72, 11, 16);
        foreach (var header in menuHeaders) header.FontSize = menuFontSize;
        foreach (var control in menuTextControls) control.FontSize = menuFontSize;
    }

    UIElement TopMenu()
    {
        menuLayer = new StackPanel { Orientation = Orientation.Horizontal, Background = SoftKeyKeyDesign.WindowSurface,
            Visibility = secureMode ? Visibility.Collapsed : Visibility.Visible };
        menuHeaders[0] = MenuHeader("Arkiv", 0);
        if (!secureMode)
        {
            updateIndicator = CreateUpdateDot();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
            header.Children.Add(new TextBlock { Text = "Arkiv", VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(updateIndicator, 1);
            header.Children.Add(updateIndicator);
            menuHeaders[0].Padding = new Thickness(8, 3, 3, 3);
            menuHeaders[0].Content = header;
            RefreshUpdateIndicator();
        }
        menuHeaders[1] = MenuHeader("Tangentbord", 1);
        menuHeaders[2] = MenuHeader("Visa", 2);
        menuHeaders[3] = MenuHeader("Mus", 3);
        for (int i = 0; i < menuHeaders.Length; i++)
        {
            if (secureMode) menuHeaders[i].Visibility = Visibility.Collapsed;
            menuLayer.Children.Add(menuHeaders[i]);
        }

        menuPanel = new Border
        {
            Background = MenuSurface,
            BorderBrush = MenuPanelStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            MinWidth = 176,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, Opacity = 0.12, BlurRadius = 3,
                ShadowDepth = 1, Direction = 315
            }
        };
        menuPopup = new Popup
        {
            Child = menuPanel, PlacementTarget = menuLayer, Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = CenterMenu,
            StaysOpen = true, AllowsTransparency = true
        };

        RefreshTopBar();
        return menuLayer;
    }

    static Border CreateUpdateDot() => new()
    {
        Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
        Background = SoftKeyKeyDesign.SlateAccent,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false, SnapsToDevicePixels = true
    };

    static Border CreateNewMenuBadge() => new()
    {
        Background = ActionSurface, CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        Child = new TextBlock
        {
            Text = "Ny", FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = ActionText, VerticalAlignment = VerticalAlignment.Center
        }
    };

    void RefreshUpdateIndicator()
    {
        if (updateIndicator == null) return;
        bool available = availableUpdate != null;
        updateIndicator.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        menuHeaders[0].ToolTip = available ? "En SoftKey-uppdatering är tillgänglig" : null;
        AutomationProperties.SetName(menuHeaders[0], available ? "Arkiv, uppdatering tillgänglig" : "Arkiv");
        if (updateMenuBadge != null)
        {
            updateMenuBadge.Visibility = updateIndicator.Visibility;
            AutomationProperties.SetName(updateMenuAction, available ? "Uppdatering…, ny uppdatering tillgänglig" : "Uppdatering…");
        }
    }

    void RefreshTopBar()
    {
        for (int i = 0; i < menuHeaders.Length; i++)
        {
            if (menuHeaders[i] == null) continue;
            bool active = i == openMenu;
            menuHeaders[i].Background = active ? ActiveMenu : SoftKeyKeyDesign.WindowSurface;
            menuHeaders[i].Foreground = !menuLayer.IsEnabled ? SystemColors.GrayTextBrush
                : active ? ActiveMenuText : SoftKeyKeyDesign.PrimaryText;
            menuHeaders[i].BorderBrush = active ? ActiveMenuBorder : Brushes.Transparent;
        }
        RefreshUpdateIndicator();
    }

    Button MenuHeader(string label, int index)
    {
        var button = new Button
        {
            Content = label, Foreground = SoftKeyKeyDesign.PrimaryText,
            Background = SoftKeyKeyDesign.WindowSurface, BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1), Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0), FontSize = 13,
            FontWeight = FontWeights.Normal, Focusable = false, Template = MenuHeaderTemplate
        };
        button.Click += (_, _) => ToggleMenu(index);
        button.MouseEnter += (_, _) =>
        {
            if (!button.IsEnabled) return;
            if (openMenu >= 0 && openMenu < menuHeaders.Length && openMenu != index) OpenMenu(index);
            else if (openMenu == index) RefreshTopBar();
            else
            {
                button.Background = KeyboardHeaderHidden ? KeyHover : MenuHeaderHover;
                button.Foreground = SoftKeyKeyDesign.PrimaryText;
                button.BorderBrush = KeyboardHeaderHidden ? ActiveMenuBorder : MenuStroke;
            }
        };
        button.MouseLeave += (_, _) => RefreshTopBar();
        return button;
    }

    void ToggleMenu(int index)
    {
        if (openMenu == index && menuPopup.IsOpen) { CloseMenu(); return; }
        OpenMenu(index);
    }

    void OpenMenu(int index)
    {
        if (secureMode) return;
        var header = menuHeaders[index];
        OpenMenu(index, header.TransformToAncestor(menuLayer).TransformBounds(new Rect(header.RenderSize)));
    }

    void ToggleWordMenu(Point point)
    {
        if (secureMode) return;
        if (openMenu == WordMenuIndex) { CloseMenu(); return; }
        OpenMenu(WordMenuIndex, new Rect(point.X, point.Y - 3, 1, 6));
    }

    void OpenMenu(int index, Rect target)
    {
        if (desktopSessionSuspended || secureMode) return;
        openMenu = index;
        menuTextControls.Clear();
        menuPanel.Child = BuildMenuPanel(index);
        menuPopup.PlacementTarget = index == WordMenuIndex ? wordMenuAnchor : menuLayer;
        // Ändrad PlacementRectangle flyttar även en meny som redan är öppen.
        menuPopup.PlacementRectangle = target;
        menuPanel.Visibility = Visibility.Visible;
        menuPopup.IsOpen = true;
        RefreshTopBar();
    }

    static CustomPopupPlacement[] CenterMenu(Size popupSize, Size targetSize, Point offset)
    {
        double left = (targetSize.Width - popupSize.Width) / 2;
        return new[]
        {
            new CustomPopupPlacement(new Point(left, targetSize.Height), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(left, -popupSize.Height), PopupPrimaryAxis.Horizontal)
        };
    }

    void CloseMenu()
    {
        if (openMenu < 0) return;
        openMenu = -1;
        menuPopup.IsOpen = false;
        menuPanel.Visibility = Visibility.Collapsed;
        menuPanel.Child = null;
        updateMenuBadge = null;
        updateMenuAction = null;
        menuTextControls.Clear();
        RefreshTopBar();
    }

    void SetMenuAvailable(bool available)
    {
        if (!available) CloseMenu();
        menuLayer.IsEnabled = available;
        wordMenuAnchor.IsEnabled = available;
        RefreshTopBar();
    }

    UIElement BuildMenuPanel(int index)
    {
        var items = new StackPanel();
        updateMenuBadge = null;
        updateMenuAction = null;
        if (index == 0)
        {
            updateMenuAction = MenuAction("Uppdatering…", OpenUpdateSettings);
            updateMenuBadge = CreateNewMenuBadge();
            DockPanel.SetDock(updateMenuBadge, Dock.Right);
            updateMenuAction.Content = new DockPanel
            {
                Children = { updateMenuBadge, new TextBlock { Text = "Uppdatering…", VerticalAlignment = VerticalAlignment.Center } }
            };
            RefreshUpdateIndicator();
            items.Children.Add(updateMenuAction);
            items.Children.Add(MenuAction("Startup…", OpenStartupSettings));
            items.Children.Add(MenuSeparator());
            items.Children.Add(MenuAction("Avsluta", Close));
        }
        else if (index == KeyboardMenuIndex)
        {
            items.Children.Add(MenuAction("Inställningar…", OpenKeyboardSettings));
            items.Children.Add(MenuAction("Ordlista…", OpenWordList));
        }
        else if (index == 2)
        {
            var alwaysOnTop = SoftKeyCheckBox.Menu("Alltid överst", menuFontSize);
            alwaysOnTop.IsChecked = cfg.AlwaysOnTop;
            alwaysOnTop.ToolTip = "Håll SoftKey framför vanliga programfönster.";
            menuTextControls.Add(alwaysOnTop);
            alwaysOnTop.Click += (_, _) => { ToggleAlwaysOnTop(); CloseMenu(); };
            items.Children.Add(alwaysOnTop);
        }
        else if (index == WordMenuIndex)
        {
            string word = currentWord;
            string label = $"Spara ”{word}”";
            var save = MenuAction(label, () => SaveWord(word));
            save.Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis };
            save.MaxWidth = 336;
            save.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(save, label);
            items.Children.Add(save);
        }
        else
        {
            items.Children.Add(MenuAction("Autoklick…", OpenMouseSettings));
            items.Children.Add(MenuSeparator());
            items.Children.Add(MenuAction(DragKeyWindow.DisplayName + "…", OpenDragKeySettings));
        }
        return items;
    }

    static Border MenuSeparator() => new()
    {
        Height = 1, Background = MenuSeparatorBrush,
        Margin = new Thickness(8, 3, 8, 3), IsHitTestVisible = false
    };

    Button MenuAction(string label, Action action)
    {
        var button = new Button
        {
            Content = label, Template = MenuActionTemplate,
            Background = MenuSurface, Foreground = Brushes.Black,
            BorderThickness = new Thickness(0),
            Padding = MenuRowPadding, Margin = MenuRowMargin,
            FontSize = menuFontSize, FontWeight = FontWeights.Normal,
            MinHeight = MenuRowHeight, Focusable = false
        };
        menuTextControls.Add(button);
        button.MouseEnter += (_, _) => SetMenuButtonState(button, button.IsPressed, true);
        button.PreviewMouseLeftButtonDown += (_, _) => SetMenuButtonState(button, true, true);
        button.PreviewMouseLeftButtonUp += (_, _) => SetMenuButtonState(button, false, button.IsMouseOver);
        button.MouseLeave += (_, _) => SetMenuButtonState(button, false, false);
        button.Click += (_, _) => { CloseMenu(); action(); };
        return button;
    }

    static void SetMenuButtonState(Button button, bool pressed, bool hovered)
    {
        button.Background = pressed ? MenuPressed : hovered ? MenuHover : MenuSurface;
    }

    void InitializeMenuDismissHook()
    {
        outsideClickProc = OutsideClickHook;
        outsideClickHook = SetWindowsHookEx(14, outsideClickProc, IntPtr.Zero, 0);
        if (outsideClickHook == IntPtr.Zero)
        {
            using var process = Process.GetCurrentProcess();
            outsideClickHook = SetWindowsHookEx(14, outsideClickProc, GetModuleHandle(process.MainModule.ModuleName), 0);
        }
        if (outsideClickHook == IntPtr.Zero)
            Trace.WriteLine($"Unable to install outside-click hook. Win32 error: {Marshal.GetLastWin32Error()}");
    }

    void DisposeMenuDismissHook()
    {
        if (outsideClickHook != IntPtr.Zero) UnhookWindowsHookEx(outsideClickHook);
        outsideClickHook = IntPtr.Zero;
    }

    delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct MouseHookData
    {
        public Native.POINT Point;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string moduleName);

    IntPtr OutsideClickHook(int code, IntPtr message, IntPtr data)
    {
        // Managed exceptions must not escape through a native Windows callback.
        try
        {
            if (code < 0 || data == IntPtr.Zero || Dispatcher.HasShutdownStarted)
                return CallNextHookEx(outsideClickHook, code, message, data);
            const int WmMouseMove = 0x0200;
            if (message.ToInt32() == WmMouseMove && mouseActions.IsWindowDragging)
                TrackWindowDragPointer(Marshal.PtrToStructure<MouseHookData>(data).Point);
            const int WmLeftButtonDown = 0x0201, WmRightButtonDown = 0x0204, WmMiddleButtonDown = 0x0207;
            if ((openMenu >= 0 || currentWord.Length > 0 || ShowingEmailSuggestions)
                && message.ToInt32() is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown)
            {
                var mouse = Marshal.PtrToStructure<MouseHookData>(data);
                if ((currentWord.Length > 0 || ShowingEmailSuggestions) && !Native.IsSoftKeyWindowAt(mouse.Point))
                {
                    currentWord = "";
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ClearFocusedSuggestions();
                        RefreshWordFeedback();
                    }));
                }
                if (openMenu >= 0)
                {
                    Point screen = new(mouse.Point.X, mouse.Point.Y);
                    bool inPopup = IsPointInside(menuPanel, screen);
                    bool inHeader = Array.Exists(menuHeaders, header => IsPointInside(header, screen));
                    bool inWordAnchor = message.ToInt32() == WmLeftButtonDown && IsPointOnWordMenuAnchor(screen);
                    if (!inPopup && !inHeader && !inWordAnchor) Dispatcher.BeginInvoke(CloseMenu);
                }
            }
        }
        catch (Exception ex) { Trace.WriteLine($"Mouse callback failed: {ex.GetType().Name}"); }
        return CallNextHookEx(outsideClickHook, code, message, data);
    }

    bool IsPointerOnMenu(Native.POINT pointer)
    {
        if (!Native.IsSoftKeyWindowAt(pointer)) return false;
        Point screen = new(pointer.X, pointer.Y);
        return Array.Exists(menuHeaders, header => IsPointInside(header, screen))
            || IsPointOnWordMenuAnchor(screen)
            || IsPointInside(wordListWin, screen)
            || IsPointInside(wordListWin?.RemovalConfirmation, screen)
            || IsPointInside(startupWin, screen)
            || IsPointInside(keyboardSettingsWin, screen)
            || IsPointInside(dragKeySettingsWin, screen)
            || IsPointInside(updateWindow, screen)
            || (openMenu >= 0 && IsPointInside(menuPanel, screen));
    }

    bool IsPointOnWordMenuAnchor(Point screen) => CanSaveCurrentWord && IsPointInside(wordMenuAnchor, screen);

    static bool IsPointInside(FrameworkElement element, Point screen)
    {
        if (element == null || !element.IsVisible || PresentationSource.FromVisual(element) == null) return false;
        Point p = element.PointFromScreen(screen);
        return p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
    }
}
