using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

partial class KeyboardWindow
{
    Button passwordSuggestionAction;
    PasswordSuggestionWindow passwordPicker;
    DesktopWordField completedPasswordField;
    Func<IntPtr, CancellationToken, Task<WindowsHelloResult>> verifyPassword = WindowsHelloConsent.Verify;
    CancellationTokenSource passwordVerification;
    bool EnteringWindowsHello => passwordVerification is { IsCancellationRequested: false };

    async void OpenPasswordPicker()
    {
        if (secureMode || !cfg.SpecificSuggestionsEnabled || desktopSessionSuspended || mainWindowActionsLocked || passwordVerification != null || !Native.CanSendInput()
            || focusedSuggestionField is not { IsPassword: true } field || field == completedPasswordField) return;
        var previous = Native.ForegroundWindow;
        if (savedTextFieldReader.ReadInsertionField() != field) return;
        if (!ReleaseHeldModifiers() || !CancelMouseAction()) return;

        var choices = savedTextStore.Read().Where(entry => entry.Kind == SavedTextKind.Password)
            .Select(entry => (entry.Id, entry.Name)).ToArray();
        var picker = new PasswordSuggestionWindow(id => FillPassword(id, previous, field))
            { Owner = this, Topmost = cfg.AlwaysOnTop };
        passwordPicker = picker;
        picker.Closed += (_, _) =>
        {
            passwordPicker = null;
            SetMainWindowActionsAvailable(true);
            if (!desktopSessionSuspended)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (desktopSessionSuspended) return;
                    Native.RestoreForegroundWindow(previous);
                    SuppressCurrentDwell();
                }));
        };
        SetMainWindowActionsAvailable(false);
        SuppressCurrentDwell();
        try
        {
            picker.Show();
            if (choices.Length == 0) { picker.ShowEntries(choices); return; }
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            if (passwordPicker != picker || desktopSessionSuspended || !Native.CanSendInput()) return;
            var result = await VerifySavedPasswords(picker);
            if (passwordPicker != picker || desktopSessionSuspended || !picker.IsVisible) return;
            if (result == WindowsHelloResult.Cancelled) picker.Close();
            else if (!result.AllowsPasswordUse() || !Native.CanSendInput())
                picker.SetStatus("Windows Hello kunde inte verifiera dig. Stäng listan och försök igen.");
            else
            {
                picker.ShowEntries(choices);
                SuppressCurrentDwell();
            }
        }
        catch
        {
            passwordPicker = null;
            SetMainWindowActionsAvailable(true);
            throw;
        }
    }

    async Task<WindowsHelloResult> VerifySavedPasswords(Window owner)
    {
        if (passwordVerification != null) return WindowsHelloResult.Unavailable;
        using var request = new CancellationTokenSource();
        passwordVerification = request;
        // Discard any dwell already in progress before the Windows dialog opens.
        desktopDwellTargets?.Leave();
        dwellTracker.Reset();
        RefreshDialogKeys();
        void Cancel(object sender, EventArgs e) => request.Cancel();
        owner.Closed += Cancel;
        try
        {
            var result = await verifyPassword(new WindowInteropHelper(owner).Handle, request.Token);
            return request.IsCancellationRequested ? WindowsHelloResult.Cancelled : result;
        }
        finally
        {
            owner.Closed -= Cancel;
            passwordVerification = null;
            ReleaseHeldModifiersCore(false);
            RefreshDialogKeys();
            if (!desktopSessionSuspended) SuppressCurrentDwell();
        }
    }

    static bool IsPasswordPromptKey(KeyDef key) => KeyboardLayout.IsPrintable(key)
        || key.Label is "Space" or "BkSp" or "Del" or "Hme" or "End" or "←" or "→"
            or "Sft" or "AltGr" or "Cap" or "Enter" or "Esc" or "Tab";

    async Task<string> FillPassword(Guid id, IntPtr previous, DesktopWordField field)
    {
        const string targetChanged = "Välj lösenordsfältet och öppna listan igen.";
        var picker = passwordPicker;
        if (!cfg.SpecificSuggestionsEnabled || desktopSessionSuspended || picker is not { IsVisible: true, IsUnlocked: true } || !Native.CanSendInput()) return targetChanged;
        if (!ReleaseHeldModifiers() || Native.ModifierDown) return "Släpp de aktiva tangenterna och försök igen.";
        if (!Native.RestoreForegroundWindow(previous)) return targetChanged;

        await Dispatcher.Yield(DispatcherPriority.Background);
        var target = (field.Active, field.Focus);
        if (!cfg.SpecificSuggestionsEnabled || desktopSessionSuspended || passwordPicker != picker || !picker.IsVisible || !Native.CanSendInput()
            || Native.InputTarget() != target)
            return targetChanged;

        var entry = savedTextStore.Read().FirstOrDefault(item => item.Id == id && item.Kind == SavedTextKind.Password);
        if (entry == null) return "Lösenordet finns inte längre bland sparade förslag.";
        var value = SavedTextStore.Open(entry);
        if (value == null) return "Kunde inte öppna det sparade lösenordet.";
        try
        {
            if (savedTextFieldReader.ReadInsertionField() != field) return targetChanged;
            if (!Native.InsertSavedText(value, target, replaceValue: true))
                return "Windows kunde inte fylla i lösenordet.";
            completedPasswordField = field;
            ShowStatusMessage(null);
            return null;
        }
        finally { CryptographicOperations.ZeroMemory(value); }
    }
}
