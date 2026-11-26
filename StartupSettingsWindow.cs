using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

class StartupSettingsWindow : Window
{
    readonly ISoftKeyStartupService service;
    readonly SoftKeyCheckBox login = SoftKeySettingsDesign.CheckOption("Visa SoftKey före inloggning",
        "Använd tangentbordet på lås- och inloggningsskärmen, även för PIN-kod och lösenord.",
        SoftKeySettingsDesign.Icon(SoftKeySettingsDesign.LockGlyph));
    readonly SoftKeyCheckBox automatic = SoftKeySettingsDesign.CheckOption("Starta SoftKey när du loggar in",
        "Öppna skrivbordstangentbordet automatiskt när du loggar in i Windows.",
        SoftKeySettingsDesign.Icon("M 13,6 A 3,3 0 1 1 7,6 A 3,3 0 1 1 13,6 Z M 3,17 C 3,10 17,10 17,17"));
    readonly SoftKeyCheckBox afterUnlock = SoftKeySettingsDesign.CheckOption("Starta SoftKey när du låser upp datorn",
        "Öppna skrivbordstangentbordet igen efter att datorn har varit låst.",
        SoftKeySettingsDesign.Icon("M 14,10 V 7 A 4,4 0 0 0 6.3,5.5 M 6,10 H 14 Q 16,10 16,12 V 16 Q 16,18 14,18 H 6 Q 4,18 4,16 V 12 Q 4,10 6,10 Z M 10,13 V 15"));
    readonly Button ok = SoftKeyButtonDesign.Action("OK", true);
    readonly Button apply = SoftKeyButtonDesign.Apply();
    readonly Button cancel = SoftKeyButtonDesign.Action("Avbryt");
    readonly ProgressBar progress = new() { Height = 4, Minimum = 0, Maximum = 100, Foreground = SoftKeyPopupDesign.Accent,
        Background = SoftKeyPopupDesign.TitleBarSurface, BorderThickness = new Thickness(0), Visibility = Visibility.Hidden };
    readonly TextBlock status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = SoftKeyPopupDesign.PrimaryText,
        Margin = new Thickness(0, 7, 0, 0), MinHeight = 16 };
    bool busy;
    StartupChoices savedChoices;
    int operation;
    internal bool IsApplying => busy;
    public StartupSettingsWindow(ISoftKeyStartupService service)
    {
        this.service = service;
        Title = "Startup";
        ShowInTaskbar = false; ShowActivated = false;
        SourceInitialized += (_, _) => Native.NoActivate(new WindowInteropHelper(this).Handle);
        login.Focusable = automatic.Focusable = afterUnlock.Focusable = ok.Focusable = apply.Focusable = cancel.Focusable = false;
        AutomationProperties.SetName(progress, "Ändrar startinställningar");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var heading = SoftKeySettingsDesign.Heading("När SoftKey ska visas");
        login.MinHeight = automatic.MinHeight = afterUnlock.MinHeight = 70;
        login.Margin = automatic.Margin = new Thickness(0, 0, 0, 9);
        var options = new StackPanel { Children = { heading, login, automatic, afterUnlock } };
        var scroll = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var footer = new StackPanel();
        footer.Children.Add(progress); footer.Children.Add(status);
        var actions = SoftKeyButtonDesign.Row(cancel, apply, ok);
        actions.Margin = new Thickness(0, 10, 0, 0);
        footer.Children.Add(actions);
        var panel = SoftKeyPopupDesign.Sections(SoftKeySettingsDesign.Width, scroll, footer);
        Content = SoftKeyPopupDesign.Frame(Title, panel, Close, () => { if (!busy) DragMove(); });
        SoftKeySettingsDesign.FitDialog(this, panel, scroll, 175, cancel, apply, ok);
        ok.IsDefault = true; cancel.IsCancel = true;
        ok.Click += async (_, _) => await ApplyAsync(true);
        apply.Click += async (_, _) => await ApplyAsync(false);
        cancel.Click += (_, _) => Close();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        try
        {
            ShowChoices(service.Read());
            status.Text = "Du kan kombinera startvalen.";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message; status.Foreground = Brushes.Firebrick;
            ok.IsEnabled = apply.IsEnabled = login.IsEnabled = automatic.IsEnabled = afterUnlock.IsEnabled = false;
        }
        foreach (var option in new[] { login, automatic, afterUnlock })
        {
            option.Checked += (_, _) => RefreshApplyButton();
            option.Unchecked += (_, _) => RefreshApplyButton();
        }
        RefreshApplyButton();
    }

    StartupChoices ReadChoices() => new(login.IsChecked == true, automatic.IsChecked == true, afterUnlock.IsChecked == true);

    void RefreshApplyButton() => SoftKeyButtonDesign.SetApplyState(apply, savedChoices != null && ReadChoices() != savedChoices, busy);

    internal async Task ApplyAsync(bool closeWhenFinished)
    {
        if (busy || savedChoices == null) return;
        var desired = ReadChoices();
        if (desired == savedChoices)
        {
            if (closeWhenFinished) Close();
            return;
        }
        int currentOperation = ++operation;
        SetBusy(true); status.Foreground = SoftKeyPopupDesign.PrimaryText; status.Text = "Förbereder startinställningarna…";
        bool succeeded = false;
        try
        {
            var updates = new Progress<StartupProgress>(p =>
            {
                if (!busy || operation != currentOperation) return;
                progress.IsIndeterminate = p.Percent <= 0 && !p.Complete;
                progress.Value = Math.Clamp(p.Percent, 0, 100);
                status.Text = p.Message;
            });
            var actual = await service.ApplyAsync(desired, updates);
            if (actual != desired)
            {
                if (actual != null) savedChoices = actual;
                throw new InvalidOperationException("Windows kunde inte bekräfta de valda inställningarna.");
            }
            ShowChoices(actual);
            status.Text = "Inställningarna har verkställts.";
            succeeded = true;
        }
        catch (Exception ex)
        {
            status.Foreground = Brushes.Firebrick;
            status.Text = ex is OperationCanceledException ? "Ändringen tog för lång tid. Kontrollera startinställningarna innan du försöker igen." : ex.Message;
        }
        finally { SetBusy(false); }
        if (succeeded && closeWhenFinished) Close();
    }
    void ShowChoices(StartupChoices choices)
    {
        savedChoices = choices;
        login.IsChecked = choices.Login;
        automatic.IsChecked = choices.Automatic;
        afterUnlock.IsChecked = choices.AfterUnlock;
    }
    void SetBusy(bool value)
    {
        busy = value;
        login.IsEnabled = automatic.IsEnabled = afterUnlock.IsEnabled = ok.IsEnabled = cancel.IsEnabled = !value;
        ok.Opacity = cancel.Opacity = value ? 0.5 : 1;
        RefreshApplyButton();
        progress.Visibility = value ? Visibility.Visible : Visibility.Hidden;
        progress.IsIndeterminate = value; progress.Value = 0;
    }
}
