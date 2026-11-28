using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

partial class KeyboardWindow
{
    readonly DesktopWordFieldReader desktopWordFieldReader = new();
    Func<DesktopWordField> readDesktopWordField;
    Func<(IntPtr Active, IntPtr Focus), Task<(DesktopWordField Field, string EmailPrefix)>> beginDesktopWordFieldRead;
    Func<(IntPtr Active, IntPtr Focus)> readDesktopTypingTarget = Native.InputTarget;
    readonly DesktopWordFieldReader displayedWordFieldReader = new();
    DispatcherTimer desktopTypingTimer;
    IntPtr desktopCapsHook;
    LowLevelMouseProc desktopCapsProc;
    bool capsKeyDown, checkingDisplayedWord;
    CancellationTokenSource emailInsertion;
    int focusVersion;
    DesktopWordField focusedSuggestionField;
    string[] emailSuggestions = Array.Empty<string>();
    string emailPrefix = "";
    (DesktopWordField Field, string Address) completedEmail;

    bool ShowingEmailSuggestions => cfg.SpecificSuggestionsEnabled && focusedSuggestionField?.Kind == DesktopInputKind.Email;

    void StartDesktopTypingTracking()
    {
        desktopCapsProc = TrackDesktopCapsLock;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        desktopCapsHook = SetWindowsHookEx(13, desktopCapsProc, GetModuleHandle(process.MainModule.ModuleName), 0);
        desktopTypingTimer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(200) };
        desktopTypingTimer.Tick += PollDesktopTyping;
        desktopTypingTimer.Start();
    }

    void StopDesktopTypingTracking()
    {
        desktopTypingTimer?.Stop();
        pendingWordKeys = null;
        desktopDwellTargets?.Leave();
        if (desktopCapsHook != IntPtr.Zero) UnhookWindowsHookEx(desktopCapsHook);
        desktopCapsHook = IntPtr.Zero;
    }

    IntPtr TrackDesktopCapsLock(int code, IntPtr message, IntPtr data)
    {
        // WH_KEYBOARD_LL provides the Windows virtual key as the first field.
        if (code >= 0 && data != IntPtr.Zero && Marshal.ReadInt32(data) == 0x14)
        {
            int kind = message.ToInt32();
            if (kind is 0x0101 or 0x0105) capsKeyDown = false;
            else if ((kind is 0x0100 or 0x0104) && !capsKeyDown)
            {
                capsKeyDown = true;
                capsLockOn = !capsLockOn;
            }
        }
        return CallNextHookEx(desktopCapsHook, code, message, data);
    }

    async void PollDesktopTyping(object sender, EventArgs e)
    {
        if (inputRecoveryPending || desktopSessionSuspended || !IsVisible) return;
        if (desktopCapsHook == IntPtr.Zero) capsLockOn = Native.CapsLockOn;
        RefreshKeyboardLabels();
        if (checkingDisplayedWord || mainWindowActionsLocked) return;
        var target = Native.InputTarget();
        if (completedPasswordField is { } completed && target != (completed.Active, completed.Focus))
            completedPasswordField = null;
        if (focusedSuggestionField is { } field && target != (field.Active, field.Focus)) SetFocusedSuggestionField(null);
        int version = focusVersion;
        var check = displayedWordFieldReader.BeginRead(target, includePassword: cfg.SpecificSuggestionsEnabled,
            includeEmailPrefix: cfg.SpecificSuggestionsEnabled);
        checkingDisplayedWord = true;
        try
        {
            var actual = check != null && await Task.WhenAny(check, Task.Delay(160)) == check ? await check : default;
            if (!Dispatcher.HasShutdownStarted && IsVisible && !desktopSessionSuspended && !mainWindowActionsLocked
                && focusVersion == version && Native.InputTarget() == target)
                SetFocusedSuggestionField(actual.Field, actual.EmailPrefix);
        }
        catch (Exception ex) when (!SoftKeyResilience.IsFatal(ex))
        {
            System.Diagnostics.Trace.TraceWarning("SoftKey desktop typing poll skipped ({0}).", ex.GetType().Name);
        }
        finally { checkingDisplayedWord = false; }
    }

    void ClearFocusedSuggestions()
    {
        pendingWordKeys = null;
        focusVersion++;
        SetFocusedSuggestionField(null);
    }

    void SetFocusedSuggestionField(DesktopWordField field, string prefix = null)
    {
        if (completedEmail.Address != null && completedEmail.Field == field
            && prefix != null && prefix != completedEmail.Address)
            completedEmail = default;
        if (focusedSuggestionField == field)
        {
            if (!ShowingEmailSuggestions || prefix == null || emailPrefix == prefix) return;
            emailPrefix = prefix;
            RefreshWordFeedback();
            return;
        }
        if (field != null && field != completedPasswordField) completedPasswordField = null;
        focusedSuggestionField = field;
        focusVersion++;
        emailPrefix = ShowingEmailSuggestions ? prefix ?? "" : "";
        emailSuggestions = ShowingEmailSuggestions ? ReadEmailSuggestions() : Array.Empty<string>();
        if (wordField != field)
        {
            currentWord = "";
            wordField = null;
            wordTarget = default;
        }
        RefreshWordFeedback();
    }

    string[] ReadEmailSuggestions()
    {
        var addresses = new List<string>(sugg.Length);
        foreach (var entry in savedTextStore.Read())
        {
            if (entry.Kind != SavedTextKind.Email) continue;
            var bytes = SavedTextStore.Open(entry);
            if (bytes == null) continue;
            try
            {
                string address = Encoding.Unicode.GetString(bytes);
                if (MailAddress.TryCreate(address, out var parsed) && parsed.Address == address && !addresses.Contains(address))
                    addresses.Add(address);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        return addresses.ToArray();
    }

    async void PickEmail(int index)
    {
        if (inputRecoveryPending || !ShowingEmailSuggestions || emailInsertion != null || focusedSuggestionField is not { Kind: DesktopInputKind.Email } target
            || sugg[index].Content is not string { Length: > 0 } address) return;
        if (lockedModifiers.Count > 0)
        {
            ShowStatusMessage("Lås upp aktiva tangenter för att välja en e-postadress");
            return;
        }
        if (!ReleaseOneShotModifiers()) return;
        using var insertion = new CancellationTokenSource();
        emailInsertion = insertion;
        try
        {
            bool sent = await DesktopWordFieldReader.WriteEmail(target, address, insertion.Token);
            if (insertion.IsCancellationRequested || !IsVisible || mainWindowActionsLocked || desktopSessionSuspended) return;
            if (sent)
            {
                completedEmail = (target, address);
                // Ignore a focus sample that started before the address was filled in.
                focusVersion++;
            }
            if (focusedSuggestionField != target || !ReportInputResult(sent)) return;
            emailPrefix = address;
            RefreshWordFeedback();
        }
        catch (Exception ex) when (!SoftKeyResilience.IsFatal(ex))
        {
            System.Diagnostics.Trace.TraceWarning("SoftKey email insertion failed ({0}).", ex.GetType().Name);
            if (IsVisible && !desktopSessionSuspended) ReportInputResult(false);
        }
        finally { emailInsertion = null; }
    }
}
