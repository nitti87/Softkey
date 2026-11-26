using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

sealed class DragKeySettingsWindow : Window
{
    public DragKeySettingsWindow(Settings settings, Func<bool, bool, bool, Task<bool>> save)
    {
        Title = DragKeyWindow.DisplayName;
        ShowActivated = ShowInTaskbar = false;
        Topmost = settings.AlwaysOnTop;
        SourceInitialized += (_, _) => Native.NoActivate(new WindowInteropHelper(this).Handle);

        var rightDrag = Option(DragKeyWindow.RightDragLabel, "Håller ned höger musknapp.", DragKeySymbol.RightDrag);
        var middleDrag = Option(DragKeyWindow.MiddleDragLabel, "Håller ned mushjulet som en knapp.", DragKeySymbol.MiddleDrag);
        rightDrag.IsChecked = settings.RightDragEnabled;
        middleDrag.IsChecked = settings.MiddleDragEnabled;
        var message = new TextBlock
        {
            Foreground = Brushes.Firebrick, Margin = new Thickness(10, 4, 10, 2), Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        var options = new StackPanel();
        options.Children.Add(SoftKeySettingsDesign.Heading("Dragknappar"));
        options.Children.Add(new TextBlock
        {
            Text = "Vänsterdrag finns alltid på panelen.", FontSize = 12, Foreground = SoftKeyPopupDesign.ActionText,
            Margin = new Thickness(1, -3, 0, 10), TextWrapping = TextWrapping.Wrap
        });
        rightDrag.Margin = new Thickness(0, 0, 0, 8);
        options.Children.Add(rightDrag);
        options.Children.Add(middleDrag);
        options.Children.Add(SoftKeyPopupDesign.Separator(new Thickness(1, 14, 1, 13)));
        options.Children.Add(SoftKeySettingsDesign.Heading("Panelstorlek"));

        var normalSize = SizeOption("Normal", small: false);
        var smallSize = SizeOption("Liten", small: true);
        normalSize.IsChecked = !settings.SmallDragKeyIcons;
        smallSize.IsChecked = settings.SmallDragKeyIcons;
        normalSize.Margin = new Thickness(0, 0, 0, 8);
        options.Children.Add(normalSize);
        options.Children.Add(smallSize);
        options.Children.Add(message);

        var cancel = SoftKeyButtonDesign.Action("Avbryt");
        var apply = SoftKeyButtonDesign.Apply(primary: true);
        cancel.IsCancel = apply.IsDefault = true;
        cancel.Click += (_, _) => Close();
        bool saving = false, closed = false, signInPending = false;
        Closing += (_, e) => { if (saving && Owner?.IsVisible == true) e.Cancel = true; };
        Closed += (_, _) => closed = true;
        foreach (var option in new ToggleButton[] { rightDrag, middleDrag, smallSize })
        {
            option.Checked += (_, _) => RefreshApplyButton();
            option.Unchecked += (_, _) => RefreshApplyButton();
        }

        (bool Right, bool Middle, bool Small) ReadChoices() => (rightDrag.IsChecked == true, middleDrag.IsChecked == true, smallSize.IsChecked == true);
        (bool Right, bool Middle, bool Small) StoredChoices() => (settings.RightDragEnabled, settings.MiddleDragEnabled, settings.SmallDragKeyIcons);
        bool HasChanges() => signInPending || ReadChoices() != StoredChoices();
        void RefreshApplyButton() => SoftKeyButtonDesign.SetApplyState(apply, HasChanges(), saving);

        apply.Click += async (_, _) =>
        {
            if (saving || !HasChanges()) return;
            var choices = ReadChoices();
            var previous = StoredChoices();
            saving = true;
            message.Visibility = Visibility.Collapsed;
            cancel.IsEnabled = options.IsEnabled = false;
            RefreshApplyButton();
            bool saved = false;
            try
            {
                saved = await save(choices.Right, choices.Middle, choices.Small);
                if (!saved) message.Text = "Kunde inte spara inställningarna. Försök igen.";
            }
            catch (InvalidOperationException ex) { message.Text = ex.Message; }
            finally
            {
                saving = false;
                // Desktop settings can be saved before Windows accepts the sign-in setting.
                signInPending = !saved && (signInPending || StoredChoices() != previous);
                if (!closed)
                {
                    cancel.IsEnabled = options.IsEnabled = true;
                    RefreshApplyButton();
                }
            }
            if (closed) return;
            if (saved) Close();
            else message.Visibility = Visibility.Visible;
        };

        var scroll = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = SoftKeyPopupDesign.Sections(SoftKeySettingsDesign.Width, scroll, SoftKeyButtonDesign.Row(cancel, apply));
        Content = SoftKeyPopupDesign.Frame(Title, panel, Close, () => { if (!saving) DragMove(); });
        SoftKeySettingsDesign.FitDialog(this, panel, scroll, 132, cancel, apply);
    }

    static RadioButton SizeOption(string label, bool small)
    {
        var preview = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        foreach (var symbol in new[] { DragKeySymbol.AutoClick, DragKeySymbol.RightClick })
        {
            var icon = DragKeyButton.CreateSymbol(symbol);
            icon.Margin = new Thickness(2, 0, 2, 0);
            DragKeyButton.SetIconSize(icon, small);
            preview.Children.Add(icon);
        }
        return SoftKeySettingsDesign.RadioOption("DragKeySize", label,
            small ? "15 % mindre ikoner och tätare knappar." : "Panelen visas i standardstorlek.",
            SoftKeySettingsDesign.Icon(preview));
    }

    static SoftKeyCheckBox Option(string label, string description, DragKeySymbol symbol)
        => SoftKeySettingsDesign.CheckOption(label, description, SoftKeySettingsDesign.Icon(DragKeyButton.CreateSymbol(symbol)));
}
