using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Threading;

partial class KeyboardWindow
{
    SavedTextStore savedTextStore;
    readonly DesktopWordFieldReader savedTextFieldReader = new();

    void OpenWordList()
    {
        if (secureMode || desktopSessionSuspended) return;
        if (wordListWin != null) { wordListWin.Activate(); return; }
        if (mainWindowActionsLocked || !ReleaseHeldModifiers()) return;
        var previous = Native.ForegroundWindow;
        var target = Native.InputTarget();
        var field = savedTextFieldReader.ReadInsertionField();
        bool hasTarget = target.Focus != IntPtr.Zero && !Native.OwnsWindow(target.Focus);
        var editor = new TabbedWordListWindow(() => words.Keys, AddWord, RemoveWord, RenameWord,
            savedTextStore, hasTarget, field?.IsPassword == true,
            entry => UseSavedText(entry, previous, target, field), cfg.SpecificSuggestionsEnabled, SaveSpecificSuggestionsEnabled)
            { Owner = this, Topmost = cfg.AlwaysOnTop, ShowInTaskbar = false };
        wordListWin = editor;
        ((System.Windows.UIElement)editor.Content).IsEnabledChanged += (_, _) => RefreshDialogKeys();
        editor.InputAvailabilityChanged += () =>
        {
            RefreshDialogKeys();
            SuppressCurrentDwell();
        };
        editor.Closed += (_, _) =>
        {
            ReleaseHeldModifiers();
            wordListWin = null;
            SetMainWindowActionsAvailable(true);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Native.RestoreForegroundWindow(previous)));
        };
        SetMainWindowActionsAvailable(false);
        try { editor.Show(); }
        catch { wordListWin = null; SetMainWindowActionsAvailable(true); throw; }
    }

    bool SaveSpecificSuggestionsEnabled(bool enabled)
    {
        if (secureMode || desktopSessionSuspended) return false;
        bool previous = cfg.SpecificSuggestionsEnabled;
        cfg.SpecificSuggestionsEnabled = enabled;
        if (!SaveSettings())
        {
            cfg.SpecificSuggestionsEnabled = previous;
            return false;
        }
        if (!enabled)
        {
            emailInsertion?.Cancel();
            passwordVerification?.Cancel();
            passwordPicker?.Close();
            phoneNumberWindow?.Close();
            completedEmail = default;
            completedPasswordField = null;
        }
        ClearFocusedSuggestions();
        SuppressCurrentDwell();
        return true;
    }

    async Task<string> UseSavedText(SavedTextEntry entry, IntPtr previous, (IntPtr Active, IntPtr Focus) target,
        DesktopWordField field)
    {
        const string targetChanged = "Välj fältet i programmet och öppna ordlistan igen.";
        var editor = wordListWin;
        if (secureMode || !cfg.SpecificSuggestionsEnabled || desktopSessionSuspended || editor is not { IsVisible: true, IsSpecific: true }
            || !SavedTextStore.IsSpecificKind(entry.Kind) || target.Focus == IntPtr.Zero
            || Native.OwnsWindow(target.Focus) || (entry.Kind == SavedTextKind.Password && field?.IsPassword != true))
            return targetChanged;
        if (!ReleaseHeldModifiers() || Native.ModifierDown) return "Släpp de aktiva tangenterna och försök igen.";
        if (entry.Kind == SavedTextKind.Password)
        {
            var result = await VerifySavedPasswords(editor);
            if (!cfg.SpecificSuggestionsEnabled || desktopSessionSuspended || wordListWin != editor || !editor.IsSpecific) return targetChanged;
            if (result == WindowsHelloResult.Cancelled) return "Verifieringen avbröts.";
            if (!result.AllowsPasswordUse()) return "Windows Hello kunde inte verifiera dig. Försök igen.";
        }
        if (!Native.RestoreForegroundWindow(previous)) return targetChanged;

        await Dispatcher.Yield(DispatcherPriority.Background);
        if (!cfg.SpecificSuggestionsEnabled || desktopSessionSuspended || wordListWin != editor || !editor.IsSpecific || Native.InputTarget() != target
            || savedTextFieldReader.ReadInsertionField() != field)
            return targetChanged;

        return InsertSavedText(entry, target);
    }

    string InsertSavedText(SavedTextEntry entry, (IntPtr Active, IntPtr Focus) target)
    {
        var value = SavedTextStore.Open(entry);
        if (value == null) return "Kunde inte öppna förslaget.";
        try
        {
            if (!Native.InsertSavedText(value, target)) return "Windows kunde inte fylla i förslaget.";
            ShowStatusMessage(null);
            ClearWordBuffer();
            return null;
        }
        finally { CryptographicOperations.ZeroMemory(value); }
    }
}
