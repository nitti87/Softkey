using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
        phoneNumberKey = Btn(PhoneNumberWindow.ChooseLabel, SoftKeyKeyDesign.SecondaryText, OpenPhoneNumberWindow);
        var handset = new Path
        {
            Width = 14, Height = 14, Margin = new Thickness(1), Stretch = Stretch.Uniform,
            Fill = SoftKeyKeyDesign.SecondaryText,
            Data = Geometry.Parse("M 6.6,2.6 C 4.8,2.3 3,3.9 3,6 C 3,13.7 10.3,21 18,21 C 20.1,21 21.7,19.2 21.4,17.4 L 21.2,16.1 C 21.1,15.6 20.8,15.3 20.3,15.2 L 16.4,14.2 C 15.9,14.1 15.5,14.2 15.2,14.6 L 13.7,16.1 C 11.2,14.8 9.2,12.8 7.9,10.3 L 9.4,8.8 C 9.8,8.5 9.9,8.1 9.8,7.6 L 8.8,3.7 C 8.7,3.2 8.4,2.9 7.9,2.8 Z")
        };
        phoneNumberKey.Content = new Viewbox { Width = 16, Height = 16, Child = handset, IsHitTestVisible = false };
        RefreshButtonVisual(phoneNumberKey, false);
        phoneNumberKey.ToolTip = PhoneNumberWindow.ChooseLabel;
        phoneNumberKey.Visibility = Visibility.Hidden;
        AutomationProperties.SetName(phoneNumberKey, PhoneNumberWindow.ChooseLabel);

        var separator = SoftKeyPopupDesign.Separator(new Thickness(7, 0, 7, 0));
        separator.VerticalAlignment = VerticalAlignment.Center;
        separator.SetBinding(VisibilityProperty,
            new System.Windows.Data.Binding(nameof(Visibility)) { Source = phoneNumberKey });
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
