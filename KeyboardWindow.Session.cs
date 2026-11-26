using System;
using System.Collections.Generic;
using System.Windows;

partial class KeyboardWindow
{
    DesktopSessionMonitor desktopSessionMonitor;
    KeyboardStartupDisplay desktopStartupDisplay;
    readonly DesktopSessionState desktopSessionState = new();
    readonly DesktopWindowVisibility desktopWindowVisibility = new();
    Func<bool?> desktopSessionLocked;
    Func<bool> desktopSessionActive;
    Func<bool> previousNativeInputGuard;
    bool previousActiveDesktopRequirement;
    bool desktopSessionSuspended;
    bool desktopSessionClosing;

    internal void EnableDesktopSessionTracking(Func<bool?> locked = null, Func<bool> active = null, Action startupReady = null)
    {
        if (secureMode || desktopSessionMonitor != null) return;
        desktopSessionLocked = locked ?? (() => LockSession.IsLocked);
        desktopSessionActive = active ?? (() => DesktopContext.IsCurrentInputDesktop);
        previousNativeInputGuard = Native.AdditionalInputGuard;
        previousActiveDesktopRequirement = Native.RequireActiveDesktop;
        desktopSessionSuspended = true;
        dwellTimer.Stop(); StopTopmostEnforcement();
        Native.RequireActiveDesktop = true;
        Native.AdditionalInputGuard = () => !desktopSessionClosing && !desktopSessionSuspended && desktopSessionLocked() == false
            && (previousNativeInputGuard?.Invoke() ?? true);
        if (startupReady != null) desktopStartupDisplay = new KeyboardStartupDisplay(this, startupReady);
        desktopSessionMonitor = new DesktopSessionMonitor(this, NotifyDesktopSession, PollDesktopSession);
    }

    void NotifyDesktopSession(int reason)
    {
        if (reason == DesktopSessionMonitor.SessionLock)
        {
            CloseOnDesktopLock();
            return;
        }
        desktopSessionState.Notify(reason);
        PollDesktopSession();
    }

    void PollDesktopSession()
    {
        if (desktopSessionClosing) return;
        bool? locked = desktopSessionLocked();
        if (locked == true)
        {
            CloseOnDesktopLock();
            return;
        }
        if (desktopSessionState.ShouldSuspend(locked, desktopSessionActive()))
        {
            SuspendDesktopSession();
            return;
        }
        if (!desktopSessionSuspended)
        {
            desktopStartupDisplay?.Observe(true);
            return;
        }
        desktopSessionSuspended = false;
        CancelMouseActionCore(false);
        ReleaseHeldModifiersCore(false);
        desktopWindowVisibility.Resume();
        if (IsLoaded) { OpenDragKey(); UpdateDragKeyPresentation(); }
        if (cfg.AlwaysOnTop) StartTopmostEnforcement();
        dwellTracker.Reset();
        RefreshDragKey();
        UpdateDwellTimer();
        SuppressCurrentDwell();
        desktopStartupDisplay?.Observe(true);
    }

    void CloseOnDesktopLock()
    {
        if (desktopSessionClosing) return;
        desktopSessionClosing = true;
        closeConfirmed = true;
        try { SuspendDesktopSession(); }
        finally { Application.Current.Shutdown(); }
    }

    void SuspendDesktopSession()
    {
        if (!desktopSessionSuspended)
        {
            CaptureWindowPositions();
            desktopSessionSuspended = true;
            passwordVerification?.Cancel();
            passwordPicker?.Close();
            phoneNumberWindow?.Close();
            completedPasswordField = null;
            CloseMenu(); ClearFocusedSuggestions(); ClearWordBuffer();
            mouseWin?.ResetDwellPreview();
            dwellTimer.Stop(); StopTopmostEnforcement();
            StopWindowDragTracking(); CancelMouseActionCore(false);
        }
        // Also hide any dialog completed/opened by an already-running asynchronous operation.
        desktopWindowVisibility.Suspend(DesktopWindows());
    }

    IEnumerable<Window> DesktopWindows()
    {
        var pending = new Stack<Window>();
        var seen = new HashSet<Window>();
        pending.Push(this);
        if (dragKeyWin != null) pending.Push(dragKeyWin);
        while (pending.Count > 0)
        {
            var window = pending.Pop();
            if (!seen.Add(window)) continue;
            yield return window;
            foreach (Window owned in window.OwnedWindows) pending.Push(owned);
        }
    }

    void StopDesktopSessionTracking()
    {
        if (desktopSessionMonitor == null) return;
        desktopSessionMonitor.Dispose(); desktopSessionMonitor = null;
        desktopWindowVisibility.Clear();
        Native.AdditionalInputGuard = previousNativeInputGuard;
        Native.RequireActiveDesktop = previousActiveDesktopRequirement;
    }
}
