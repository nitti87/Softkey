using System;
using System.Threading.Tasks;
using System.Windows.Threading;

partial class KeyboardWindow
{
    KeyboardSettingsWindow keyboardSettingsWin;
    readonly bool useInstalledLayout;
    bool IsKeyboardLayoutSaving => keyboardSettingsWin?.IsSaving == true;

    void OpenKeyboardSettings()
    {
        if (secureMode || desktopSessionSuspended || mainWindowActionsLocked || !CancelMouseAction() || !ReleaseHeldModifiers()) return;
        IntPtr previousForeground = Native.ForegroundWindow;
        keyboardSettingsWin = new KeyboardSettingsWindow(cfg, useInstalledLayout, SaveKeyboardLayoutAsync) { Owner = this };
        keyboardSettingsWin.Closed += (_, _) =>
        {
            keyboardSettingsWin = null;
            SetMainWindowActionsAvailable(true);
            SuppressCurrentDwell();
            if (!desktopSessionSuspended)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Native.RestoreForegroundWindow(previousForeground)));
        };
        SetMainWindowActionsAvailable(false);
        try { keyboardSettingsWin.Show(); }
        catch
        {
            keyboardSettingsWin = null;
            SetMainWindowActionsAvailable(true);
            throw;
        }
    }

    async Task<bool> SaveKeyboardLayoutAsync(KeyboardLayoutSettings layout, bool applyToSignIn, bool menuAbove)
    {
        if (secureMode || desktopSessionSuspended || !layout.IsValid) return false;
        if (applyToSignIn && !useInstalledLayout) return false;
        if (!SaveDesktopKeyboardLayout(layout, applyToSignIn, menuAbove)) return false;
        if (applyToSignIn)
        {
            UpdateLayout();
            try { await StartupClient.ApplyKeyboardLayoutAsync(layout, CurrentKeyboardArea()); }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"Inställningarna sparades för skrivbordet. {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.IO.IOException
                or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                throw new InvalidOperationException("Inställningarna sparades för skrivbordet. Windows-inloggningen kunde inte bekräftas. Försök igen.", ex);
            }
        }
        return true;
    }

    bool SaveDesktopKeyboardLayout(KeyboardLayoutSettings layout, bool shareLayout, bool menuAbove)
    {
        var previous = KeyboardLayoutSettings.From(cfg);
        bool wasPersonal = cfg.HasPersonalKeyboardLayout;
        bool wasSharing = cfg.ShareKeyboardLayoutAtSignIn;
        bool wasAbove = cfg.MenuAboveDragKey;
        bool titleHidden = KeyboardHeaderHidden;
        if (!TrySetDesktopHeader(titleHidden, menuAbove))
            throw new InvalidOperationException("Kunde inte ändra menyplaceringen. Försök igen.");
        if (wasPersonal && previous == layout && wasSharing == shareLayout && wasAbove == menuAbove)
            return true;

        layout.ApplyTo(cfg);
        cfg.HasPersonalKeyboardLayout = true;
        cfg.ShareKeyboardLayoutAtSignIn = shareLayout;
        bool saved = false;
        try { saved = SaveSettings(); }
        finally
        {
            previous.ApplyTo(cfg);
            cfg.HasPersonalKeyboardLayout = saved || wasPersonal;
            if (!saved)
            {
                cfg.ShareKeyboardLayoutAtSignIn = wasSharing;
                TrySetDesktopHeader(titleHidden, wasAbove);
            }
        }
        if (saved) ChangeKeyboardLayout(layout);
        return saved;
    }

    internal void RefreshSignInSettings()
    {
        ChangeKeyboardLayout(KeyboardLayoutSettings.ReadInstalled() ?? KeyboardLayoutSettings.Default);
        var area = KeyboardAreaSize.ReadInstalled();
        ApplySignInDragKeySize(SignInDragKeySettings.ReadSmallSize(), area);
        ApplySignInKeyboardArea(area);
        if (AutoclickTimingSettings.ReadInstalled() is { } timing && timing != AutoclickTimingSettings.From(cfg))
        {
            timing.ApplyTo(cfg);
            SuppressCurrentDwell();
        }
    }

    void ChangeKeyboardLayout(KeyboardLayoutSettings layout)
    {
        if (layout == KeyboardLayoutSettings.From(cfg)) return;
        layout.ApplyTo(cfg);
        UpdateKeyboardMinimumSize();
        ApplyKeyboardSpacing();
        if (keyboardKeyGrid.ActualHeight > 0 && keyboardKeyGrid.ActualWidth > 0) ScaleText(keyboardKeyGrid);
    }
}
