using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

partial class KeyboardWindow
{
    Button phoneNumberKey;
    PhoneNumberWindow phoneNumberWindow;

    void BuildPhoneNumberKeySlot(Grid navigation)
    {
        if (secureMode) return;
        int separatorRow = navigation.RowDefinitions.Count - 2;
        phoneNumberKey = Btn(PhoneNumberWindow.ChooseLabel, SoftKeyKeyDesign.SlateAccentText, OpenPhoneNumberWindow);
        var handset = PhoneNumberWindow.PhoneIcon(20);
        handset.Stretch = Stretch.None;
        handset.SetBinding(Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = phoneNumberKey });
        phoneNumberKey.Content = new Viewbox { Width = 20, Height = 20, Child = handset, IsHitTestVisible = false };
        RefreshButtonVisual(phoneNumberKey, false);
        phoneNumberKey.ToolTip = PhoneNumberWindow.ChooseLabel;
        phoneNumberKey.Visibility = Visibility.Hidden;
        AutomationProperties.SetName(phoneNumberKey, PhoneNumberWindow.ChooseLabel);

        var separator = SoftKeyPopupDesign.Separator(new Thickness(7, 0, 7, 0));
        separator.VerticalAlignment = VerticalAlignment.Center;
        separator.SetBinding(VisibilityProperty,
            new Binding(nameof(Visibility)) { Source = phoneNumberKey });
        Grid.SetRow(separator, separatorRow);
        Grid.SetRow(phoneNumberKey, separatorRow + 1);
        navigation.Children.Add(separator);
        navigation.Children.Add(phoneNumberKey);
    }

    void RefreshPhoneNumberKey()
    {
        if (phoneNumberKey == null) return;
        phoneNumberKey.Visibility = cfg.SpecificSuggestionsEnabled && !desktopSessionSuspended && !mainWindowActionsLocked
            && focusedSuggestionField is { CanInsertText: true } ? Visibility.Visible : Visibility.Hidden;
    }

    void OpenPhoneNumberWindow()
    {
        if (phoneNumberKey?.IsVisible != true || !Native.CanSendInput()) return;
        IntPtr previous = Native.ForegroundWindow;
        var field = focusedSuggestionField;
        if (field is not { CanInsertText: true } || Native.InputTarget() != (field.Active, field.Focus))
        {
            ClearFocusedSuggestions();
            return;
        }
        if (!CancelMouseAction() || !ReleaseHeldModifiers()) return;

        var picker = new PhoneNumberWindow(ReadSavedPhoneNumbers(), number => UseSavedPhoneNumber(number.Entry, previous, field))
            { Owner = this, Topmost = cfg.AlwaysOnTop };
        phoneNumberWindow = picker;
        picker.InputAvailabilityChanged += RefreshDialogKeys;
        picker.Closed += (_, _) =>
        {
            ReleaseHeldModifiersCore(!desktopSessionSuspended);
            phoneNumberWindow = null;
            SetMainWindowActionsAvailable(true);
            if (!desktopSessionSuspended)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Native.RestoreForegroundWindow(previous)));
        };
        SetMainWindowActionsAvailable(false);
        SuppressCurrentDwell();
        try
        {
            picker.Show();
            RefreshDialogKeys();
        }
        catch
        {
            phoneNumberWindow = null;
            SetMainWindowActionsAvailable(true);
            throw;
        }
    }

    SavedPhoneNumber[] ReadSavedPhoneNumbers()
    {
        var numbers = new List<SavedPhoneNumber>();
        foreach (var entry in savedTextStore.Read())
        {
            if (entry.Kind != SavedTextKind.Phone) continue;
            byte[] bytes = SavedTextStore.Open(entry);
            if (bytes == null) continue;
            try
            {
                if (bytes.Length > 1024 || (bytes.Length & 1) != 0) continue;
                string number = Encoding.Unicode.GetString(bytes);
                if (SavedTextStore.IsPhoneNumber(number)) numbers.Add(new(entry, number));
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        return numbers.ToArray();
    }

    async Task<string> UseSavedPhoneNumber(SavedTextEntry entry, IntPtr previous, DesktopWordField field)
    {
        const string targetChanged = "Välj skrivfältet och öppna telefonlistan igen.";
        var picker = phoneNumberWindow;
        if (!cfg.SpecificSuggestionsEnabled || desktopSessionSuspended || picker?.IsVisible != true || !Native.CanSendInput())
            return targetChanged;
        if (!ReleaseHeldModifiers() || Native.ModifierDown) return "Släpp de aktiva tangenterna och försök igen.";
        if (!Native.RestoreForegroundWindow(previous)) return targetChanged;

        await Dispatcher.Yield(DispatcherPriority.Background);
        var target = (field.Active, field.Focus);
        bool TargetIsCurrent() => cfg.SpecificSuggestionsEnabled && !desktopSessionSuspended && phoneNumberWindow == picker
            && picker.IsVisible && Native.InputTarget() == target;
        if (!TargetIsCurrent()) return targetChanged;
        var reading = savedTextFieldReader.BeginRead(target, includePassword: true);
        if (reading == null || await Task.WhenAny(reading, Task.Delay(600)) != reading
            || (await reading).Field != field || !TargetIsCurrent()) return targetChanged;

        return InsertSavedText(entry, target);
    }
}
