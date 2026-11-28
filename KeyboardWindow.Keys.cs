using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

// Vad som händer när du klickar på en tangent, samt ordförslagen.
partial class KeyboardWindow
{
    const string WordListFile = "words.json";

    Dictionary<string, int> words;   // ordlista: ord -> hur ofta det använts
    string currentWord = "";
    string statusMessage;
    (IntPtr Active, IntPtr Focus) wordTarget;
    DesktopWordField wordField;
    PendingWordKeys pendingWordKeys;
    Func<KeyDef, bool, bool> modifierInput = (key, up) => Native.Key(key.Scan, key.Ext, up, key.Vk);
    Func<KeyDef, bool> keyInput = key => Native.PressKey(key.Scan, key.Ext, key.Vk);

    sealed class PendingWordKeys(DesktopWordField field, int focusVersion, Task<(DesktopWordField Field, string EmailPrefix)> check)
    {
        public DesktopWordField Field { get; } = field;
        public int FocusVersion { get; } = focusVersion;
        public Task<(DesktopWordField Field, string EmailPrefix)> Check { get; } = check;
        public List<(KeyDef Key, bool Plain, string Text)> Keys { get; } = new();
    }

    bool HasWordOnStatusRow => !secureMode && statusMessage == null && currentWord.Length > 0;
    bool CanSaveCurrentWord => !mainWindowActionsLocked && pendingWordKeys == null
        && HasWordOnStatusRow && !words.ContainsKey(currentWord.ToLower());

    Dictionary<string, int> LoadWordList()
    {
        var savedWords = storage.Load<Dictionary<string, int>>(WordListFile, null);
        if (savedWords != null) return savedWords;

        using var defaults = typeof(KeyboardWindow).Assembly.GetManifestResourceStream(WordListFile);
        return JsonSerializer.Deserialize<Dictionary<string, int>>(defaults);
    }

    void RefreshStatusText()
    {
        bool showingWord = HasWordOnStatusRow;
        if (openMenu == WordMenuIndex && (!showingWord || statusText.Text != currentWord)) CloseMenu();
        statusText.Text = showingWord ? currentWord : statusMessage ?? (secureMode ? "Inloggning" : "Redo");
    }

    void ShowStatusMessage(string message)
    {
        statusMessage = message;
        RefreshStatusText();
    }

    bool ReportInputResult(bool sent)
    {
        ShowStatusMessage(sent ? null : "Windows kunde inte skicka inmatningen");
        return sent;
    }

    // Anropas när en tangent klickas.
    void Press(KeyDef k)
    {
        if (startupWin != null || keyboardSettingsWin != null || dragKeySettingsWin != null) return;
        if (inputRecoveryPending || desktopSessionSuspended || (secureMode && !inputDesktopActive())) return;
        if (!secureMode && mainWindowActionsLocked && !btn[k].IsEnabled) return;
        bool verifyingPassword = passwordVerification != null;
        var pinWindow = verifyingPassword ? Native.WindowsHelloInputWindow() : IntPtr.Zero;
        if (verifyingPassword && (!EnteringWindowsHello || !IsPasswordPromptKey(k) || pinWindow == IntPtr.Zero)) return;
        if (SecureAttention.IsChord(k, held)) { RequestSecureAttention(); return; }
        FlashKey(k);
        // Håll både tangentbordet och en öppen meny ovanpå när en tangent trycks.
        KeepSoftKeyOnTop();
        if (mouseWin != null && mouseWin.TryType(k, RequestSecureAttention)) return;
        if (!verifyingPassword && wordListWin != null && !wordListWin.FocusInputFromKeyboard()) return;
        if (phoneNumberWindow != null && !phoneNumberWindow.FocusSearchInput()) return;

        if (k.Mod) // första klicket aktiverar, andra låser, tredje låser upp
        {
            if (verifyingPassword && Native.ForegroundWindow != pinWindow) return;
            if (!held.Contains(k))
            {
                if (ReportInputResult(modifierInput(k, false))) held.Add(k);
            }
            else if (lockedModifiers.Add(k))
            {
                // Andra klicket låser modifieraren; den är redan nedtryckt i Windows.
            }
            else if (ReportInputResult(modifierInput(k, true)))
            {
                held.Remove(k);
                lockedModifiers.Remove(k);
            }
            UpdateKeyVisual(k);
            RefreshKeyboardLabels();
            return;
        }
        if (!secureMode) RefreshKeyboardLabels();
        bool shortcut = held.Any(m => m.Label is "Ctl" or "Alt" or "Win");
        bool plain = !shortcut && !held.Any(m => m.Label == "AltGr");
        string keyText = !secureMode && !shortcut && KeyboardLayout.IsPrintable(k) ? btn[k].Content as string : null;
        PendingWordKeys batch = null;
        DesktopWordField target = null;
        if (pendingWordKeys is { Check.IsCompletedSuccessfully: true } ready)
            ApplyPendingWordKeys(ready, ready.Check.Result.Field);
        if (!secureMode && !mainWindowActionsLocked)
        {
            bool checkField = !shortcut && (currentWord.Length != 0 || pendingWordKeys != null
                || keyText is { Length: 1 } && char.IsLetter(keyText[0]));
            if (checkField)
            {
                // Recheck confirmed text fields in the background. Keep the original preflight on
                // unknown fields and on Enter, which can submit the form and move focus immediately.
                if (k.Label != "Enter" && focusedSuggestionField is { Kind: DesktopInputKind.Text } known
                    && readDesktopTypingTarget() == (known.Active, known.Focus))
                    batch = PrepareWordKeys(known);
                else
                {
                    target = readDesktopWordField();
                    if (target != null) SetFocusedSuggestionField(target);
                }
            }
        }
        if (verifyingPassword && Native.ForegroundWindow != pinWindow) return;
        bool sent = ReportInputResult(keyInput(k));
        if (sent && !secureMode && completedPasswordField is { } filled && filled == focusedSuggestionField
            && Native.InputTarget() == (filled.Active, filled.Focus) && (keyText != null || k.Label is "BkSp" or "Del"))
            completedPasswordField = null;
        if (sent && k.Label == "Cap" && (secureMode || desktopCapsHook == IntPtr.Zero)) capsLockOn = !capsLockOn;
        bool released = ReleaseOneShotModifiers();
        if (secureMode || mainWindowActionsLocked || !sent || !released) { ClearWordBuffer(); return; }
        if (batch == null)
        {
            ApplyWordKey(target, k, plain, keyText);
            return;
        }
        if (batch.Keys.Count >= 32) { ClearWordBuffer(); return; }
        batch.Keys.Add((k, plain, keyText));
        if (batch.Keys.Count == 1) _ = ReadPendingWordKeys(batch);
    }

    PendingWordKeys PrepareWordKeys(DesktopWordField field)
    {
        if (pendingWordKeys is { } pending && pending.Field == field && pending.FocusVersion == focusVersion)
            return pending;
        var check = beginDesktopWordFieldRead((field.Active, field.Focus));
        return pendingWordKeys = check == null ? null : new(field, focusVersion, check);
    }

    async Task ReadPendingWordKeys(PendingWordKeys batch)
    {
        var field = await Task.WhenAny(batch.Check, Task.Delay(DesktopWordFieldReader.WordCheckTimeoutMs)) == batch.Check
            ? (await batch.Check).Field : null;
        ApplyPendingWordKeys(batch, field);
    }

    void ApplyPendingWordKeys(PendingWordKeys batch, DesktopWordField field)
    {
        if (pendingWordKeys != batch) return;
        pendingWordKeys = null;
        if (desktopSessionSuspended || mainWindowActionsLocked || batch.FocusVersion != focusVersion
            || field != batch.Field || readDesktopTypingTarget() != (field.Active, field.Focus))
        {
            ClearWordBuffer();
            return;
        }
        foreach (var key in batch.Keys) ApplyWordKey(field, key.Key, key.Plain, key.Text);
    }

    void ApplyWordKey(DesktopWordField target, KeyDef key, bool plain, string text)
    {
        if (target?.Kind != DesktopInputKind.Text) { ClearWordBuffer(); return; }
        if (wordField != target) ClearWordBuffer();
        wordField = target;
        wordTarget = (target.Active, target.Focus);
        Track(key, plain, text);
    }

    // One-shot modifiers survive other modifier clicks, then release after the next regular key.
    bool ReleaseOneShotModifiers()
    {
        bool released = true;
        foreach (var m in held.Where(m => !lockedModifiers.Contains(m)).ToArray())
        {
            if (modifierInput(m, true))
            {
                held.Remove(m);
                UpdateKeyVisual(m);
            }
            else released = false;
        }
        if (!released) ReportInputResult(false);
        RefreshKeyboardLabels();
        return released;
    }

    // Följer det aktuella ordet på statusraden och i ordförslagen.
    void Track(KeyDef k, bool plain, string keyText)
    {
        if (secureMode) { ClearWordBuffer(); return; }
        if (keyText is { Length: 1 } && char.IsLetter(keyText[0])) currentWord += keyText;
        else if (plain && k.Label == "BkSp")
        {
            if (currentWord.Length > 0) currentWord = currentWord[..^1];
        }
        else if (plain && k.Label is "Space" or "Enter" or "," or ".") Commit();
        else if (k.Label != "Cap") currentWord = "";
        RefreshWordFeedback();
    }

    void Commit()
    {
        if (secureMode) { ClearWordBuffer(); return; }
        if (currentWord.Length > 1 && wordTarget.Focus != IntPtr.Zero) LearnWord(currentWord.ToLower());
        currentWord = "";
    }

    void LearnWord(string word)
    {
        if (secureMode) return;
        int count = words.GetValueOrDefault(word);
        if (count < int.MaxValue) words[word] = count + 1;
    }

    void SaveWord(string word)
    {
        if (!CanSaveCurrentWord || word != currentWord || !WordFieldStillMatches()) return;
        string error = AddWord(word);
        if (error != null) ShowStatusMessage(error);
    }

    string AddWord(string word)
    {
        if (secureMode) return "Ordhantering är avstängd vid inloggning.";
        word = word.Trim();
        if (word.Length == 0 || !word.All(char.IsLetter)) return "Skriv ett ord med enbart bokstäver.";
        string key = word.ToLower();
        if (words.ContainsKey(key)) return "Ordet finns redan i ordlistan.";

        var updated = new Dictionary<string, int>(words) { [key] = 1 };
        return UpdateWordList(updated) ? null : "Kunde inte spara ordet. Försök igen.";
    }

    string RemoveWord(string word)
    {
        if (secureMode) return "Ordhantering är avstängd vid inloggning.";
        var updated = new Dictionary<string, int>(words);
        if (!updated.Remove(word)) return "Ordet finns inte längre i ordlistan.";
        return UpdateWordList(updated) ? null : "Kunde inte ta bort ordet. Försök igen.";
    }

    string RenameWord(string original, string replacement)
    {
        if (secureMode) return "Ordhantering är avstängd vid inloggning.";
        replacement = replacement.Trim();
        if (replacement.Length == 0 || !replacement.All(char.IsLetter)) return "Skriv ett ord med enbart bokstäver.";
        if (!words.TryGetValue(original, out int frequency)) return "Ordet finns inte längre i ordlistan.";
        string key = replacement.ToLower();
        if (key == original) return null;
        if (words.ContainsKey(key)) return "Ordet finns redan i ordlistan.";

        var updated = new Dictionary<string, int>(words);
        updated.Remove(original);
        updated.Add(key, frequency);
        return UpdateWordList(updated) ? null : "Kunde inte spara ändringen. Försök igen.";
    }

    bool UpdateWordList(Dictionary<string, int> updated)
    {
        if (secureMode) return false;
        if (!storage.Save(WordListFile, updated)) return false;
        words = updated;
        RefreshWordFeedback();
        return true;
    }

    void ClearWordBuffer()
    {
        pendingWordKeys = null;
        currentWord = "";
        if (!secureMode) { wordField = null; wordTarget = default; }
        RefreshWordFeedback();
    }

    bool WordFieldStillMatches()
    {
        if (wordField != null && wordField == readDesktopWordField()) return true;
        ClearWordBuffer();
        return false;
    }

    void RefreshWordFeedback()
    {
        if (secureMode) { currentWord = ""; wordTarget = default; }
        RefreshPhoneNumberKey();
        RefreshStatusText();
        string prefix = currentWord.ToLower();
        bool capitalized = currentWord.Length > 0 && char.IsUpper(currentWord[0]);
        var list = Array.Empty<string>();
        if (ShowingEmailSuggestions)
        {
            if (completedEmail.Field != focusedSuggestionField)
                list = emailSuggestions.Where(address => DesktopWordFieldReader.MatchesEmailPrefix(address, emailPrefix))
                    .Take(sugg.Length).ToArray();
        }
        else if (prefix.Length > 0 && wordTarget.Focus != IntPtr.Zero)
            list = words.Where(w => w.Key.StartsWith(prefix) && w.Key != prefix)
                .OrderByDescending(w => w.Value).ThenBy(w => w.Key.Length)
                .Take(sugg.Length).Select(w => w.Key).ToArray();
        bool changed = false;
        for (int i = 0; i < sugg.Length; i++)
        {
            var button = sugg[i];
            string word = i < list.Length ? (capitalized && !ShowingEmailSuggestions
                ? char.ToUpper(list[i][0]) + list[i][1..] : list[i]) : "";
            bool hasWord = word.Length > 0;
            changed |= !Equals(button.Content, word);
            button.Content = word;
            // Restart a dwell when a different suggestion takes over the same button.
            button.DataContext = hasWord ? word : null;
            button.IsEnabled = hasWord;
            button.Visibility = hasWord ? Visibility.Visible : Visibility.Collapsed;
            button.ToolTip = hasWord ? word : null;
            RefreshButtonVisual(button, button.IsMouseOver, button.IsPressed);
        }
        suggestionViewport.Visibility = list.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (changed) suggestionViewport.ScrollToTop();
        bool showPasswordAction = !secureMode && cfg.SpecificSuggestionsEnabled && !mainWindowActionsLocked && focusedSuggestionField?.IsPassword == true
            && focusedSuggestionField != completedPasswordField;
        if (passwordSuggestionAction != null)
            passwordSuggestionAction.Visibility = showPasswordAction ? Visibility.Visible : Visibility.Collapsed;
        emptySuggestionsIcon.Visibility = !secureMode && !showPasswordAction && list.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // Ett klick på ett förslag: ta bort det påbörjade ordet och skriv hela ordet + mellanslag.
    void PickWord(int n)
    {
        if (secureMode || mainWindowActionsLocked) return;
        if (ShowingEmailSuggestions) { PickEmail(n); return; }
        if (pendingWordKeys != null || currentWord.Length == 0 || wordTarget.Focus == IntPtr.Zero
            || sugg[n].Content is not string w || w == "") return;
        if (lockedModifiers.Count > 0)
        {
            ShowStatusMessage("Lås upp aktiva tangenter för att välja ordförslag");
            return;
        }
        if (!ReleaseOneShotModifiers()) return;
        if (Native.ModifierDown)
        {
            ShowStatusMessage("Släpp aktiva tangenter för att välja ordförslag");
            return;
        }
        if (currentWord.Length == 0 || !WordFieldStillMatches()) return;
        if (ReportInputResult(Native.ReplaceWord(currentWord.Length, w))) LearnWord(w.ToLower());
        ClearWordBuffer();
    }
}
