using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

partial class KeyboardWindow : Window
{
    const double StatusRowHeight = 23;
    const double StatusFontSize = 12;
    static readonly Brush StatusForeground = new SolidColorBrush(Color.FromRgb(0x1C, 0x25, 0x2E));
    static readonly Brush WindowSurface = new SolidColorBrush(Color.FromRgb(0xE7, 0xED, 0xF3));
    static readonly Brush KeyFace = SoftKeyPopupDesign.CardSurface;
    static readonly Brush KeyShadow = new SolidColorBrush(Color.FromRgb(0xB6, 0xC5, 0xD3));
    static readonly Brush EnterKeyHover = new SolidColorBrush(Color.FromRgb(0x9A, 0xC9, 0xEC));
    static readonly Brush EnterKeyPressed = new SolidColorBrush(Color.FromRgb(0x8C, 0xBD, 0xE2));
    static readonly Brush KeyText = new SolidColorBrush(Color.FromRgb(0x15, 0x26, 0x39));
    static readonly Brush KeyBorder = new SolidColorBrush(Color.FromRgb(0x6F, 0x83, 0x98));
    static readonly Brush KeyHover = new SolidColorBrush(Color.FromRgb(0xD1, 0xE4, 0xF5));
    static readonly Brush KeyFlash = SoftKeyPopupDesign.ActionPressed;
    static readonly Brush KeyHeld = new SolidColorBrush(Color.FromRgb(0xBC, 0xD8, 0xEF));
    static readonly Brush KeyLocked = SoftKeyPopupDesign.Accent;
    static readonly Brush KeyActiveBorder = new SolidColorBrush(Color.FromRgb(0x24, 0x5F, 0x92));
    static readonly Brush AltGrValueColor = new SolidColorBrush(Color.FromRgb(0x0C, 0x55, 0x84));
    static readonly ControlTemplate KeyTemplate = KeyButtonTemplate(raised: true);

    readonly Settings cfg;
    readonly JsonFileStorage storage;
    readonly bool secureMode;
    Func<bool> inputDesktopActive = () => DesktopContext.IsCurrentInputDesktop;
    readonly HashSet<KeyDef> held = new();            // modifiertangenter som är nedtryckta just nu
    readonly HashSet<KeyDef> lockedModifiers = new(); // modifierare som förblir aktiva efter nästa tangent
    readonly Dictionary<KeyDef, Button> btn = new();  // tangent -> dess knapp
    readonly Dictionary<KeyDef, (Brush Background, Brush Foreground)> keyBaseStyle = new();
    readonly HashSet<KeyDef> flashingKeys = new();
    readonly Button[] sugg = new Button[8];           // knapparna för ordförslag
    readonly List<Button> allButtons = new();         // alla knappar (för textstorleken)
    readonly DispatcherTimer keyFeedbackTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly DispatcherTimer topmostTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer dwellTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    TextBlock statusText;
    TextBlock modifierStatus;
    ScrollViewer suggestionViewport;
    Image emptySuggestionsIcon;
    MouseSettingsWindow mouseWin;                     // Musinställning (null = stängd)
    TabbedWordListWindow wordListWin;
    bool mainWindowActionsLocked;
    bool capsLockOn;
    IntPtr displayedKeyboardLayout;
    int displayedKeyboardLayer = -1;
    string displayedKeyboardAccent;
    bool closeConfirmed;                              // true när vi inte ska fråga (t.ex. Windows stängs av)
    CloseConfirmationWindow closeConfirmation;

    public KeyboardWindow() : this(SoftKeyStorage.Default, false, null, KeyboardLayoutSettings.IsInstalled) { }

    public KeyboardWindow(JsonFileStorage storage) : this(storage, false, null) { }

    internal void EnablePreviewClose() => closeConfirmed = true;
    internal void SetInputGuard(Func<bool> guard) => inputDesktopActive = guard;
    internal void CloseForDesktopTransition() { closeConfirmed = true; Close(); }

    public KeyboardWindow(JsonFileStorage storage, bool secureMode, Settings startupSettings, bool useInstalledLayout = false)
    {
        this.storage = storage;
        this.secureMode = secureMode;
        this.useInstalledLayout = useInstalledLayout;
        if (!secureMode)
        {
            readDesktopWordField = desktopWordFieldReader.Read;
            beginDesktopWordFieldRead = target => desktopWordFieldReader.BeginRead(target);
            savedTextStore = new SavedTextStore(storage);
        }
        capsLockOn = Native.CapsLockOn;
        cfg = secureMode ? startupSettings ?? new Settings() : storage.Load("settings.json", new Settings());
        cfg.Normalize();
        if (secureMode) cfg.AlwaysOnTop = true;
        if (useInstalledLayout && !cfg.HasPersonalKeyboardLayout) KeyboardLayoutSettings.ReadInstalled()?.ApplyTo(cfg);
        words = secureMode ? new Dictionary<string, int>() : LoadWordList();

        Title = secureMode ? "SoftKey – Inloggning" : "SoftKey";
        Focusable = false;
        Topmost = cfg.AlwaysOnTop;
        ResizeMode = ResizeMode.CanResize;   // går att dra i kanterna
        if (secureMode) { WindowStyle = WindowStyle.None; ShowInTaskbar = false; }
        UpdateKeyboardMinimumSize();
        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(cfg.WindowWidth + DockWidth(cfg.DragKeyPlacement), MinWidth, Math.Max(MinWidth, workArea.Width));
        Height = Math.Clamp(cfg.WindowHeight + DockHeight(cfg.DragKeyPlacement), MinHeight, Math.Max(MinHeight, workArea.Height));
        dragKeyReferenceSize = new Size(Width, Height);
        Background = WindowSurface;
        Foreground = SoftKeyPopupDesign.PrimaryText;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        Content = Build();
        ApplyKeyboardSpacing();
        RefreshKeyboardLabels();
        InitializePositionTracking();
        keyFeedbackTimer.Tick += (_, _) =>
        {
            keyFeedbackTimer.Stop();
            foreach (var key in new List<KeyDef>(flashingKeys))
            {
                flashingKeys.Remove(key);
                UpdateKeyVisual(key);
            }
        };

        Loaded += (_, _) =>
        {
            RestoreKeyboardPosition();
            OpenDragKey();
            if (secureMode) ApplySignInKeyboardArea(cfg.KeyboardArea);
            else if (cfg.HideKeyboardTitleBar && !TrySetDesktopHeader(true, cfg.MenuAboveDragKey)) ReportInputResult(false);
            positionTrackingReady = true;
            RememberDesktopKeyboardArea();
        };

        SizeChanged += (_, _) =>
        {
            RememberKeyboardSize();
            RefreshKeyboardMoveDestination();
        };

        // Fångar även tillståndsbyten som Windows gör utan ett systemkommando.
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Maximized
                || ((mainWindowActionsLocked || (secureMode && !closeConfirmed)) && WindowState == WindowState.Minimized))
                WindowState = WindowState.Normal;
            UpdateDragKeyPresentation();
        };

        // Windows stängs av eller användaren loggar ut: fråga inte (annars blockerar vi avstängningen).
        if (Application.Current != null) Application.Current.SessionEnding += OnSessionEnding;

        Closing += (_, e) =>
        {
            // A sign-in keyboard must remain available until Windows changes desktops.
            if (secureMode && !closeConfirmed) { e.Cancel = true; return; }
            if (!CancelMouseAction() && !closeConfirmed)
            {
                e.Cancel = true;
                return;
            }
            if (!closeConfirmed && mainWindowActionsLocked)
            {
                e.Cancel = true;
                return;
            }
            if (!closeConfirmed && !ConfirmClose(this))
            {
                e.Cancel = true;
                return;
            }
            if (!ReleaseHeldModifiers() && !closeConfirmed)
            {
                e.Cancel = true;
                return;
            }
            StopTopmostEnforcement();
            dwellTimer.Stop();
            CaptureWindowPositions();
            if (!secureMode)
            {
                storage.Save("settings.json", cfg);
                storage.Save(WordListFile, words);
            }
        };

        topmostTimer.Tick += (_, _) => KeepSoftKeyOnTop();
        dwellTimer.Tick += Dwell;   // Dwell finns i KeyboardWindow.Autoclick.cs
        ApplyAutoclickSettings();
    }

    void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        CancelMouseAction();
        closeConfirmed = true;
    }

    bool SaveSettings()
    {
        CaptureWindowPositions();
        if (secureMode) return false; // Sign-in settings are read-only; never open a user's data files.
        bool saved = storage.Save("settings.json", cfg);
        ShowStatusMessage(saved ? null : "Kunde inte spara inställningarna");
        return saved;
    }

    void UpdateKeyboardMinimumSize()
    {
        if (secureMode)
        {
            MinWidth = MinimumWindowWidth() + DockWidth(cfg.DragKeyPlacement) * WindowResizeGeometry.MaxPanelScale;
            MinHeight = MinimumWindowHeight() + DockHeight(cfg.DragKeyPlacement) * WindowResizeGeometry.MaxPanelScale
                - RemovedCompactBottomPadding;
            return;
        }
        // Reserve room for docking and for the key faces when the gaps grow.
        MinWidth = MinimumWindowWidth() + DragKeySize.Normal.SideWidth * WindowResizeGeometry.MaxPanelScale + DragKeyDocking.Gap;
        double sideHeight = DragKeySize.Normal.SideHeight(true, true);
        MinHeight = Math.Max(MinimumWindowHeight() + DragKeySize.Normal.TopHeight * WindowResizeGeometry.MaxPanelScale + DragKeyDocking.Gap,
            sideHeight * WindowResizeGeometry.MaxPanelScale + StatusRowHeight + 8 + 35
                + SystemParameters.CaptionHeight + 2 * SystemParameters.ResizeFrameHorizontalBorderHeight) - hiddenHeaderHeight;
    }

    double MinimumWindowWidth()
    {
        const double minKeyFontSize = 9;
        int gap = KeyboardLayout.ScaledGap(cfg.HorizontalKeyGap, cfg.KeyFontSizePercent);
        double keyUnitWidth = minKeyFontSize / 0.38 + Math.Max(0, gap - Settings.DefaultKeyGap);
        double weight = KeyboardLayout.TotalColumnWeight - (secureMode ? KeyboardLayout.SuggestionColumnWeight : 0);
        double clientWidth = keyUnitWidth * KeyboardLayout.KeyUnitsPerRow * weight / KeyboardLayout.KeyboardColumnWeight + 16;
        return secureMode ? clientWidth : (clientWidth + 2 * SystemParameters.ResizeFrameVerticalBorderWidth) * 1.10;
    }

    double MinimumWindowHeight()
    {
        const double minKeyFontSize = 9;
        int gap = KeyboardLayout.ScaledGap(cfg.VerticalKeyGap, cfg.KeyFontSizePercent);
        double keyRowHeight = minKeyFontSize / 0.40 + Math.Max(0, gap - Settings.DefaultKeyGap);
        double headerHeight = secureMode ? SignInMoveGripHeight : cfg.MenuAboveDragKey ? DesktopMenuRowHeight : 25;
        double clientHeight = headerHeight + 8 + KeyboardLayout.Rows.Length * keyRowHeight + StatusRowHeight;
        return secureMode ? clientHeight : (clientHeight + SystemParameters.CaptionHeight + 2 * SystemParameters.ResizeFrameHorizontalBorderHeight) * 1.10;
    }

    bool ReleaseHeldModifiers() => ReleaseHeldModifiersCore(true);
    bool ReleaseHeldModifiersCore(bool reportFailure)
    {
        foreach (var key in held.ToArray())
        {
            if (!modifierInput(key, true)) continue;
            held.Remove(key);
            lockedModifiers.Remove(key);
            UpdateKeyVisual(key);
        }
        RefreshKeyboardLabels();
        if (held.Count > 0 && reportFailure) ShowStatusMessage("Windows kunde inte släppa alla tangenter. Försök igen.");
        return held.Count == 0;
    }

    void FlashKey(KeyDef key)
    {
        flashingKeys.Add(key);
        UpdateKeyVisual(key);
        keyFeedbackTimer.Stop();
        keyFeedbackTimer.Start();
    }

    void UpdateKeyVisual(KeyDef key, bool? hovered = null, bool? pressed = null)
    {
        if (!btn.TryGetValue(key, out var button)) return;
        bool isHovered = hovered ?? button.IsMouseOver;
        bool isPressed = pressed ?? button.IsPressed;
        var style = keyBaseStyle[key];
        Brush background = style.Background;
        Brush foreground = held.Any(m => m.Label == "AltGr") && KeyboardLayout.IsPrintable(key)
            ? AltGrValueColor : style.Foreground;
        Brush border = key.Label == "Enter" ? KeyActiveBorder : KeyBorder;
        if (held.Contains(key) && lockedModifiers.Contains(key))
        {
            background = KeyLocked;
            foreground = Brushes.White;
            border = KeyLocked;
        }
        else if (held.Contains(key) || mouseWin?.IsChordModifierSelected(key) == true)
        {
            background = KeyHeld;
            foreground = SoftKeyPopupDesign.ActionText;
            border = KeyActiveBorder;
        }
        else if (flashingKeys.Contains(key))
        {
            background = key.Label == "Enter" ? EnterKeyPressed : KeyFlash;
            foreground = SoftKeyPopupDesign.ActionText;
            border = KeyActiveBorder;
        }
        else if (isHovered)
        {
            background = key.Label == "Enter" ? (isPressed ? EnterKeyPressed : EnterKeyHover)
                : isPressed ? KeyFlash : KeyHover;
            border = KeyActiveBorder;
        }
        button.Background = background;
        button.Foreground = foreground;
        button.BorderBrush = border;
        button.BorderThickness = new Thickness(1);
    }

    void RefreshKeyboardLabels()
    {
        RefreshModifierStatus();
        bool shift = held.Any(k => k.Label == "Sft");
        bool altGr = held.Any(k => k.Label == "AltGr");
        int layer = (shift ? 1 : 0) | (altGr ? 2 : 0) | (capsLockOn ? 4 : 0);
        IntPtr keyboardLayout = secureMode ? Native.CurrentKeyboardLayout : Native.DesktopInputLayout();
        string accent = Native.PendingKeyboardAccent(keyboardLayout);
        if (displayedKeyboardLayer == layer && displayedKeyboardLayout == keyboardLayout && displayedKeyboardAccent == accent) return;
        displayedKeyboardLayer = layer;
        displayedKeyboardLayout = keyboardLayout;
        displayedKeyboardAccent = accent;

        foreach (var key in btn.Keys)
        {
            if (!KeyboardLayout.IsPrintable(key)) continue;
            string value = Native.TranslateKey(keyboardLayout, key.Scan, key.Ext, shift, altGr, capsLockOn);
            if (altGr)
            {
                string ordinaryValue = Native.TranslateKey(keyboardLayout, key.Scan, key.Ext, shift, false, capsLockOn);
                btn[key].Content = !string.IsNullOrEmpty(value)
                    && !string.Equals(value, ordinaryValue, StringComparison.Ordinal)
                    ? value
                    : "";
            }
            else
            {
                btn[key].Content = string.IsNullOrEmpty(value) ? key.Label : value;
            }
            UpdateKeyVisual(key);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        Native.NoActivate(handle);
        HwndSource.FromHwnd(handle).AddHook(FilterMainWindowCommands);
        if (secureMode && !closeConfirmed) Native.SetWindowButtonsEnabled(handle, false);
        else if (WindowStyle == WindowStyle.None) Native.RecalculateWindowFrame(handle);
        if (secureMode && RemovedCompactBottomPadding > 0)
        {
            UpdateKeyboardMinimumSize();
            // A saved key area will size the frame after layout. Keep the original key height in the fallback frame.
            if (cfg.KeyboardArea is not { IsValid: true })
            {
                dragKeyReferenceSize.Height = Math.Max(MinHeight, Height - RemovedCompactBottomPadding);
                Height = dragKeyReferenceSize.Height;
            }
        }
        if (cfg.AlwaysOnTop) StartTopmostEnforcement();
        InitializeMenuDismissHook();
        if (!secureMode) StartDesktopTypingTracking();
    }

    IntPtr FilterMainWindowCommands(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (WindowStyle == WindowStyle.None && WindowState != WindowState.Minimized)
        {
            if (message == Native.WmNcActivate)
            {
                handled = true;
                return Native.ActivateWithoutRepaintingFrame(window, wParam);
            }
            if (message == Native.WmNcPaint)
            {
                // Windows can otherwise paint its old caption over the titleless client area.
                handled = true;
                return IntPtr.Zero;
            }
        }
        if (message == Native.WmNcCalcSize && WindowStyle == WindowStyle.None && lParam != IntPtr.Zero
            && UpdateTitlelessFrame(window))
        {
            // Leave the entire window rectangle to WPF. A default frame repaint can no longer shift just part of it.
            handled = true;
            return wParam == IntPtr.Zero ? IntPtr.Zero : new IntPtr(0x0300); // WVR_REDRAW
        }
        if (message == Native.WmNcHitTest && WindowStyle == WindowStyle.None && WindowState == WindowState.Normal
            && ResizeMode is (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip))
        {
            var pointer = Native.ScreenPointFromMessage(lParam);
            int edge = Native.HitTestResizeBorder(window, pointer, compactFrame);
            if (edge != 0)
            {
                handled = true;
                if (edge == Native.HtTop && !secureMode && IsDesktopHeaderAt(new Point(pointer.X, pointer.Y)))
                    return new IntPtr(Native.HtClient);
                return new IntPtr(edge);
            }
        }
        if (message is 0x0216 or 0x0214 or 0x0046 && FilterKeyboardEdges(window, message, lParam))
        {
            handled = true;
            return new IntPtr(1);
        }
        if (message == 0x0231) nativeWindowMove = true; // WM_ENTERSIZEMOVE
        if (message == 0x0232)
        {
            nativeWindowMove = false; // WM_EXITSIZEMOVE
            CompleteKeyboardTextResize();
            if (positionTrackingReady) SaveSettings();
        }
        if (SingleInstance.RestoreMessage != 0 && (uint)message == SingleInstance.RestoreMessage)
        {
            if (desktopSessionSuspended) { handled = true; return IntPtr.Zero; }
            if (keyboardHidden) ToggleKeyboardVisibility();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            if (!IsKeyboardLayoutSaving) Native.RaiseWithoutActivation(window);
            KeepSoftKeyOnTop();
            handled = true;
            return IntPtr.Zero;
        }
        if (message == 0x0047) QueueDragKeyOrder(); // WM_WINDOWPOSCHANGED: move, resize, restore or raise
        if (message == Native.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(Native.MaNoActivate);
        }
        if ((mainWindowActionsLocked || (secureMode && !closeConfirmed)) && message == Native.WmSysCommand
            && (wParam.ToInt64() & 0xFFF0) is Native.ScMinimize or Native.ScMaximize or Native.ScClose)
            handled = true;
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        positionTrackingReady = false;
        positionSaveTimer.Stop();
        CancelMouseAction();
        StopWindowDragTracking();
        dragKeyWin?.CloseWithApplication();
        dragKeyWin = null;
        keyFeedbackTimer.Stop();
        if (Application.Current != null) Application.Current.SessionEnding -= OnSessionEnding;
        CloseMenu();
        StopTopmostEnforcement();
        DisposeMenuDismissHook();
        if (!secureMode) StopDesktopTypingTracking();
        StopDesktopSessionTracking();
        base.OnClosed(e);
    }

    // ---------- Gränssnitt ----------
    static Button KeySurface(string text, Brush foreground) => new()
    {
        Content = text, Foreground = foreground, Background = KeyFace, BorderBrush = KeyBorder,
        BorderThickness = new Thickness(1), Padding = new Thickness(0.5, 0, 0.5, 0), FontWeight = FontWeights.SemiBold, FontSize = 15,
        Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Center, Template = KeyTemplate
    };

    Button Btn(string text, Brush fg, Action click)
    {
        var b = KeySurface(text, fg);
        b.MouseEnter += (_, _) => RefreshButtonVisual(b, true, b.IsPressed);
        b.MouseLeave += (_, _) => RefreshButtonVisual(b, false);
        b.PreviewMouseLeftButtonDown += (_, _) => RefreshButtonVisual(b, true, true);
        b.PreviewMouseLeftButtonUp += (_, _) => RefreshButtonVisual(b, b.IsMouseOver);
        b.LostMouseCapture += (_, _) => RefreshButtonVisual(b, b.IsMouseOver);
        b.Click += (_, _) => click();
        allButtons.Add(b);
        return b;
    }

    void RefreshButtonVisual(Button button, bool hovered, bool pressed = false)
    {
        if (button.Tag is KeyDef key) { UpdateKeyVisual(key, hovered, pressed); return; }
        if (button.Template == KeyTemplate)
        {
            button.Background = hovered ? (pressed ? SoftKeyPopupDesign.ActionPressed : KeyHover) : SoftKeyKeyDesign.FunctionFace;
            button.BorderBrush = hovered ? KeyActiveBorder : KeyBorder;
            return;
        }
        bool active = button.IsEnabled && button.Content is string word && !string.IsNullOrWhiteSpace(word);
        button.Background = active && hovered ? (pressed ? SoftKeyPopupDesign.ActionPressed : KeyHover) : Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
    }

    // En kolumn/rad som delar på utrymmet i proportion till "weight" (stjärnstorlek).
    static ColumnDefinition Col(double weight) => new() { Width = new GridLength(weight, GridUnitType.Star) };
    static RowDefinition Row1() => new() { Height = new GridLength(1, GridUnitType.Star) };

    // Textstorleken följer tangenternas storlek.
    void ScaleText(Grid g)
    {
        double rowH = g.RowDefinitions.Take(KeyboardLayout.Rows.Length).Average(r => r.ActualHeight);
        double unitW = g.ColumnDefinitions[0].ActualWidth / KeyboardLayout.KeyUnitsPerRow;
        double fs = Math.Min(rowH * 0.40, unitW * 0.38);
        double keyFontSize = KeyboardLayout.LegendFontSize(rowH, unitW);
        foreach (var b in allButtons)
        {
            if (b.Template == KeyTemplate) SetKeyLegendSize(b, keyFontSize, cfg.KeyFontSizePercent);
            else b.FontSize = fs;
        }
        statusText.FontSize = Math.Min(StatusFontSize, fs);
        ScaleMenuText(fs);
    }

    internal static void SetKeyLegendSize(Button button, double baseSize, int percent)
    {
        double textScale = button.Content is string ? SoftKeyKeyDesign.LegendTextScale : 1;
        button.FontSize = baseSize * textScale * percent / Settings.DefaultKeyFontSizePercent;
        if (button.Content is Viewbox icon)
            icon.Width = icon.Height = baseSize * 1.42;
    }

    UIElement StatusRow()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusText = new TextBlock
        {
            Text = "Redo",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = StatusFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = StatusForeground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        TextOptions.SetTextFormattingMode(statusText, TextFormattingMode.Display);
        row.Children.Add(statusText);
        if (!secureMode)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            modifierStatus = new TextBlock
            {
                Foreground = SoftKeyPopupDesign.ActionText, FontSize = StatusFontSize,
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(modifierStatus, 1);
            row.Children.Add(modifierStatus);
        }
        return new Border
        {
            Background = WindowSurface,
            BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8, 0, 6, 0),
            UseLayoutRounding = true,
            Child = row
        };
    }

    void OpenMouseSettings()
    {
        if (secureMode || desktopSessionSuspended) return;
        if (mouseWin != null)
        {
            Native.KeepOnTop(new WindowInteropHelper(mouseWin).Handle);
            return;
        }
        IntPtr previousForeground = Native.ForegroundWindow;
        if (!CancelMouseAction() || !ReleaseHeldModifiers()) return;
        mouseWin = new MouseSettingsWindow(cfg, () =>
        {
            if (!SaveSettings()) return false;
            ApplyAutoclickSettings();
            return true;
        }, useInstalledLayout ? SaveSignInAutoclickTimingAsync : null) { Owner = this };
        mouseWin.KeyAvailabilityChanged += RefreshDialogKeys;
        mouseWin.Closed += (_, _) =>
        {
            mouseWin = null;
            SetMainWindowActionsAvailable(true);
            UpdateDwellTimer();
            if (!desktopSessionSuspended)
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(() => Native.RestoreForegroundWindow(previousForeground)));
        };
        SetMainWindowActionsAvailable(false);
        try { mouseWin.Show(); UpdateDwellTimer(); }
        catch
        {
            mouseWin = null;
            SetMainWindowActionsAvailable(true);
            throw;
        }
    }

    void SetMainWindowActionsAvailable(bool available)
    {
        mainWindowActionsLocked = !available;
        if (!available)
        {
            CancelMouseAction();
            if (!secureMode) { ClearFocusedSuggestions(); ClearWordBuffer(); }
        }
        if (!secureMode) RefreshDialogKeys();
        RefreshDragKey();
        SetMenuAvailable(available);
        Native.SetWindowButtonsEnabled(new WindowInteropHelper(this).Handle, available);
    }

    void RefreshDialogKeys()
    {
        bool editingWords = wordListWin?.CanType == true || phoneNumberWindow?.CanType == true;
        foreach (var (key, button) in btn)
        {
            bool enabled = !mainWindowActionsLocked || mouseWin?.CanUseKey(key) == true
                || (EnteringWindowsHello && IsPasswordPromptKey(key))
                || (editingWords && (KeyboardLayout.IsPrintable(key) || key.Label is "Space" or "BkSp" or "Del"
                    or "Hme" or "End" or "←" or "→" or "↑" or "↓" or "Sft" or "Ctl" or "Alt" or "AltGr"
                    or "Cap" or "Enter" or "ESC" or "PrtScr"));
            button.IsEnabled = enabled;
            button.Opacity = enabled ? 1 : 0.45;
            UpdateKeyVisual(key);
        }
        RefreshModifierStatus();
    }

    void RefreshModifierStatus()
    {
        if (modifierStatus == null) return;
        var active = new List<string>(5);
        bool Active(string label) => held.Any(k => k.Label == label)
            || (mouseWin != null && btn.Keys.Any(k => k.Label == label && mouseWin.IsChordModifierSelected(k)));
        if (Active("Sft")) active.Add("Shift");
        if (Active("Alt")) active.Add("Alt");
        if (Active("Ctl")) active.Add("Ctrl");
        if (capsLockOn) active.Add("Caps Lock");
        if (Active("AltGr")) active.Add("AltGr");
        modifierStatus.Text = string.Join("  ·  ", active);
    }

    Window UtilityDialog => (Window)updateWindow ?? (Window)startupWin ?? (Window)wordListWin ?? (Window)mouseWin
        ?? (Window)keyboardSettingsWin ?? (Window)dragKeySettingsWin ?? (Window)passwordPicker ?? phoneNumberWindow;

    bool ConfirmClose(Window owner)
    {
        if (closeConfirmation != null) return false;
        IntPtr previousForeground = Native.ForegroundWindow;
        var dialog = new CloseConfirmationWindow { Owner = owner, Topmost = cfg.AlwaysOnTop };
        closeConfirmation = dialog;
        bool lockActions = !mainWindowActionsLocked;
        bool confirmed;
        try
        {
            if (lockActions) SetMainWindowActionsAvailable(false);
            confirmed = dialog.ShowDialog() == true;
        }
        finally
        {
            closeConfirmation = null;
            if (lockActions && !desktopSessionClosing && !Dispatcher.HasShutdownStarted)
                SetMainWindowActionsAvailable(true);
        }
        if (!confirmed && !desktopSessionSuspended)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Native.RestoreForegroundWindow(previousForeground)));
        return confirmed;
    }

    void ToggleAlwaysOnTop()
    {
        if (secureMode) return;
        cfg.AlwaysOnTop = !cfg.AlwaysOnTop;
        Topmost = cfg.AlwaysOnTop;
        Native.SetTopmost(new WindowInteropHelper(this).Handle, cfg.AlwaysOnTop);
        if (dragKeyWin != null)
        {
            dragKeyWin.Topmost = cfg.AlwaysOnTop;
            Native.SetTopmost(new WindowInteropHelper(dragKeyWin).Handle, cfg.AlwaysOnTop);
        }
        if (mouseWin != null)
        {
            mouseWin.Topmost = cfg.AlwaysOnTop;
            Native.SetTopmost(new WindowInteropHelper(mouseWin).Handle, cfg.AlwaysOnTop);
        }
        if (cfg.AlwaysOnTop) StartTopmostEnforcement();
        else StopTopmostEnforcement();
        KeepDragKeyAboveKeyboard();
        SaveSettings();
    }

    Button KeyBtn(KeyDef k)
    {
        var b = Btn(k.Label, k.Fg ?? KeyText, () => Press(k));
        if (k.Label == "Win")
        {
            string keyName = k.Scan == 0x5B ? "Vänster Windows" : "Höger Windows";
            b.ToolTip = keyName;
            System.Windows.Automation.AutomationProperties.SetName(b, keyName);
        }
        else if (k.Label == "Menu")
        {
            b.ToolTip = "Snabbmeny";
            System.Windows.Automation.AutomationProperties.SetName(b, "Snabbmeny");
        }
        ApplyKeyFace(b, k);
        b.Tag = k;
        btn[k] = b;
        keyBaseStyle[k] = (b.Background, b.Foreground);
        return b;
    }

    internal static Button CreatePreviewKey(KeyDef key)
    {
        var button = KeySurface(key.Label, key.Fg ?? KeyText);
        ApplyKeyFace(button, key);
        return button;
    }

    static void ApplyKeyFace(Button button, KeyDef k)
    {
        if (k.Label == "Win") button.Content = WindowsLogo();
        else if (k.Label == "Menu") button.Content = MenuKeyIcon();
        else if (k.Label is "←" or "→" or "↑" or "↓") ApplyArrowKeyIcon(button, k.Label);
        button.Background = k.Mod || k.Label == "Cap" ? SoftKeyKeyDesign.ModifierFace
            : KeyboardLayout.IsPrintable(k) || k.Label == "Space" ? KeyFace : SoftKeyKeyDesign.FunctionFace;
        if (button.Content is string { Length: 1 } || k.Label is "Enter" or "Space")
            button.FontWeight = FontWeights.Bold;
        if (k.Label == "Enter")
        {
            button.Background = SoftKeyKeyDesign.AccentFace;
            button.Foreground = SoftKeyPopupDesign.ActionText;
            button.BorderBrush = KeyActiveBorder;
        }
    }

    static void ApplyArrowKeyIcon(Button button, string label)
    {
        (double rotation, string name) = label switch
        {
            "←" => (-90, "Vänsterpil"),
            "→" => (90, "Högerpil"),
            "↓" => (180, "Nedåtpil"),
            _ => (0, "Uppåtpil")
        };
        var arrow = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 3.5,10 L 10,3.5 16.5,10 M 10,3.5 L 10,17"),
            StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = new RotateTransform(rotation, 10, 10)
        };
        arrow.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
        });
        var canvas = new Canvas { Width = 20, Height = 20, Children = { arrow } };
        button.Content = new Viewbox { Width = 20, Height = 20, Child = canvas, IsHitTestVisible = false };
        button.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(button, name);
    }

    static Viewbox WindowsLogo()
    {
        var logo = new Grid { Width = 16, Height = 16 };
        logo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        logo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        logo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        logo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 2; column++)
        {
            var pane = new Border
            {
                CornerRadius = new CornerRadius(0.7),
                Margin = new Thickness(0.75)
            };
            pane.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
            Grid.SetRow(pane, row);
            Grid.SetColumn(pane, column);
            logo.Children.Add(pane);
        }

        return new Viewbox
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Child = logo
        };
    }

    static Viewbox MenuKeyIcon()
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 2,2 L 14,2 L 14,14 L 2,14 Z M 5,5 H 11 M 5,8 H 11 M 5,11 H 9"),
            StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 16, Height = 16
        };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
        });
        return new Viewbox { Width = 16, Height = 16, Child = icon };
    }

    static ControlTemplate KeyButtonTemplate(HorizontalAlignment contentAlignment = HorizontalAlignment.Center,
        double cornerRadius = 2, bool raised = false)
    {
        var template = new ControlTemplate(typeof(Button));
        System.Windows.Data.Binding TemplateBinding(string path) => new(path)
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        };
        var border = new FrameworkElementFactory(typeof(Border), "KeyFace");
        border.SetValue(Border.CornerRadiusProperty, raised ? SoftKeyKeyDesign.Radius : new CornerRadius(cornerRadius));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        border.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
        border.SetBinding(Border.BorderBrushProperty, TemplateBinding("BorderBrush"));
        border.SetBinding(Border.BorderThicknessProperty, TemplateBinding("BorderThickness"));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding("Content"));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding("ContentTemplate"));
        if (raised)
        {
            var fittedContent = new FrameworkElementFactory(typeof(KeyboardKeyLegend));
            fittedContent.SetBinding(KeyboardKeyLegend.RequestedFontSizeProperty, TemplateBinding("FontSize"));
            fittedContent.SetValue(Viewbox.StretchDirectionProperty, StretchDirection.DownOnly);
            fittedContent.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            fittedContent.SetBinding(HorizontalAlignmentProperty, TemplateBinding("HorizontalContentAlignment"));
            fittedContent.SetBinding(MarginProperty, TemplateBinding("Padding"));
            content.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display);
            fittedContent.AppendChild(content);
            border.AppendChild(fittedContent);
            var surface = new FrameworkElementFactory(typeof(KeyboardKeySurface));
            surface.SetValue(KeyboardKeySurface.InsetProperty, new TemplateBindingExtension(KeyboardKeySurface.InsetProperty));
            template.VisualTree = SoftKeyKeyDesign.Raised(border, surface, edgeBrush: KeyShadow);
        }
        else
        {
            content.SetValue(HorizontalAlignmentProperty, contentAlignment);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetBinding(MarginProperty, TemplateBinding("Padding"));
            border.AppendChild(content);
            template.VisualTree = border;
        }
        return template;
    }
}
