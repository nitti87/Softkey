using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

class StartupSettingsWindow : Window
{
    readonly ISoftKeyStartupService service;
    readonly SoftKeyCheckBox login = SoftKeySettingsDesign.CheckOption("Visa SoftKey före inloggning",
        "Använd tangentbordet på lås- och inloggningsskärmen, även för PIN-kod och lösenord.",
        SoftKeySettingsDesign.Icon(SoftKeySettingsDesign.LockGlyph));
    readonly SoftKeyCheckBox automatic = SoftKeySettingsDesign.CheckOption("Starta SoftKey när du loggar in",
        "Öppna skrivbordstangentbordet automatiskt när du loggar in i Windows.",
        SoftKeySettingsDesign.Icon("M 13,6 A 3,3 0 1 1 7,6 A 3,3 0 1 1 13,6 Z M 3.5,17 C 3.5,10.8 16.5,10.8 16.5,17"));
    readonly SoftKeyCheckBox afterUnlock = SoftKeySettingsDesign.CheckOption("Starta SoftKey när du låser upp datorn",
        "Öppna skrivbordstangentbordet igen efter att datorn har varit låst.",
        SoftKeySettingsDesign.Icon("M 14,10 V 7 A 4,4 0 0 0 6.3,5.5 M 6,10 H 14 Q 16,10 16,12 V 16 Q 16,18 14,18 H 6 Q 4,18 4,16 V 12 Q 4,10 6,10 Z M 10,13 V 15"));
    readonly Button ok = SoftKeyButtonDesign.Action("OK", true);
    readonly Button apply = SoftKeyButtonDesign.Apply();
    readonly Button cancel = SoftKeyButtonDesign.Action("Avbryt");
    readonly ProgressBar progress = new()
    {
        Height = 6, Minimum = 0, Maximum = 100, Foreground = SoftKeyPopupDesign.Accent,
        Background = SoftKeyPopupDesign.ActionSurface, Template = SoftKeyPopupDesign.ProgressTemplate,
        BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed
    };
    readonly TextBlock status = new()
    {
        FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = SoftKeyPopupDesign.SecondaryText,
        Margin = new Thickness(0, 3, 0, 0)
    };
    readonly TextBlock activeCountText = new() { FontSize = 11, FontWeight = FontWeights.SemiBold };
    readonly Border activeCountBadge;
    readonly Border infoStrip;
    readonly Border feedbackCard;
    readonly TextBlock feedbackTitle = new()
    {
        FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        Foreground = SoftKeyPopupDesign.PrimaryText
    };
    bool busy, hasError;
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
        AutomationProperties.SetName(progress, "Ändrar startinställningarna");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        activeCountBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 1.5, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = activeCountText
        };
        var overviewCard = BuildOverviewCard(activeCountBadge);
        var heading = SoftKeySettingsDesign.Heading("När SoftKey ska visas");
        login.MinHeight = automatic.MinHeight = afterUnlock.MinHeight = 72;
        login.Margin = automatic.Margin = new Thickness(0, 0, 0, 9);

        infoStrip = BuildInfoStrip();
        feedbackCard = new Border
        {
            Background = SoftKeyPopupDesign.CardSurface,
            BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(14, 11, 14, 12),
            Margin = new Thickness(0, 11, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel { Children = { feedbackTitle, status, progress } }
        };

        var options = new StackPanel
        {
            Children = { overviewCard, heading, login, automatic, afterUnlock, infoStrip, feedbackCard }
        };
        var scroll = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var actions = SoftKeyButtonDesign.Row(cancel, apply, ok);
        var panel = SoftKeyPopupDesign.Sections(SoftKeySettingsDesign.Width, scroll, actions);
        Content = SoftKeyPopupDesign.Frame(Title, panel, Close, () => { if (!busy) DragMove(); });
        SoftKeySettingsDesign.FitDialog(this, panel, scroll, 132, cancel, apply, ok);
        SoftKeyPopupDesign.KeepResizeInWorkArea(this);
        ok.IsDefault = true; cancel.IsCancel = true;
        ok.Click += async (_, _) => await ApplyAsync(true);
        apply.Click += async (_, _) => await ApplyAsync(false);
        cancel.Click += (_, _) => Close();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        try
        {
            ShowChoices(service.Read());
            ShowIdleNote();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            ok.IsEnabled = apply.IsEnabled = login.IsEnabled = automatic.IsEnabled = afterUnlock.IsEnabled = false;
        }
        foreach (var option in new[] { login, automatic, afterUnlock })
        {
            option.Checked += (_, _) => RefreshState();
            option.Unchecked += (_, _) => RefreshState();
        }
        RefreshState();
    }

    static Border BuildOverviewCard(Border countBadge)
    {
        var iconCanvas = new Canvas { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        iconCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 10,5 H 4.5 Q 2.5,5 2.5,7 V 13.5 Q 2.5,15.5 4.5,15.5 H 12.5 Q 14.5,15.5 14.5,13.5 V 10.5 M 8.5,15.5 V 18 M 5.8,18 H 11.2 M 14.5,2 V 5.5 M 12.43,3.2 A 3.1,3.1 0 1 0 16.57,3.2"),
            Stroke = SoftKeyPopupDesign.Accent,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        var iconTile = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(9),
            Background = SoftKeyPopupDesign.ActionSurface,
            Margin = new Thickness(0, 0, 13, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = iconCanvas
        };
        DockPanel.SetDock(iconTile, Dock.Left);

        var titleRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new TextBlock
        {
            Text = "Automatisk start i Windows",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = SoftKeyPopupDesign.PrimaryText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        titleRow.Children.Add(countBadge);

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                titleRow,
                new TextBlock
                {
                    Text = "Välj när SoftKey ska öppnas automatiskt. Valen kan kombineras.",
                    FontSize = 12,
                    Foreground = SoftKeyPopupDesign.SecondaryText,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0)
                }
            }
        };
        return new Border
        {
            Background = SoftKeyPopupDesign.CardSurface,
            BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(13, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 14),
            Child = new DockPanel { Children = { iconTile, text } }
        };
    }

    static Border BuildInfoStrip()
    {
        var shieldCanvas = new Canvas
        {
            Width = 16, Height = 16, Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        shieldCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 8,1.5 L 13.5,3.7 V 8.1 C 13.5,11.4 11.1,13.7 8,14.7 C 4.9,13.7 2.5,11.4 2.5,8.1 V 3.7 Z M 8,5.4 V 8.6 M 8,11.1 V 11.3"),
            Stroke = SoftKeyPopupDesign.Accent,
            StrokeThickness = 1.45,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        DockPanel.SetDock(shieldCanvas, Dock.Left);

        var note = new TextBlock
        {
            Text = "Ändringar före inloggning kräver att du godkänner i Windows.",
            FontSize = 11.5,
            Foreground = SoftKeyPopupDesign.SecondaryText,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new Border
        {
            Background = SoftKeyKeyDesign.ModifierFace,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8.5, 12, 8.5),
            Margin = new Thickness(0, 11, 0, 0),
            Child = new DockPanel { Children = { shieldCanvas, note } }
        };
    }

    StartupChoices ReadChoices() => new(login.IsChecked == true, automatic.IsChecked == true, afterUnlock.IsChecked == true);

    void RefreshState()
    {
        int activeCount = (login.IsChecked == true ? 1 : 0)
            + (automatic.IsChecked == true ? 1 : 0)
            + (afterUnlock.IsChecked == true ? 1 : 0);
        activeCountText.Text = activeCount switch
        {
            0 => "Ingen aktiv",
            1 => "1 av 3 aktiv",
            _ => $"{activeCount} av 3 aktiva"
        };
        activeCountBadge.Background = activeCount > 0 ? SoftKeyPopupDesign.DialogFooter : SoftKeyPopupDesign.DialogSurface;
        activeCountText.Foreground = activeCount > 0 ? SoftKeyPopupDesign.PrimaryText : SoftKeyPopupDesign.SecondaryText;
        RefreshApplyButton();
    }

    void RefreshApplyButton() => SoftKeyButtonDesign.SetApplyState(apply, savedChoices != null && ReadChoices() != savedChoices, busy);

    void ShowIdleNote()
    {
        hasError = false;
        status.Foreground = SoftKeyPopupDesign.SecondaryText;
        status.Text = "Du kan kombinera startvalen.";
        progress.Visibility = Visibility.Collapsed;
        feedbackCard.Visibility = Visibility.Collapsed;
        infoStrip.Visibility = Visibility.Visible;
    }

    void ShowError(string message)
    {
        hasError = true;
        feedbackCard.Background = SoftKeyPopupDesign.ErrorSurface;
        feedbackCard.BorderBrush = SoftKeyPopupDesign.ErrorBorder;
        feedbackTitle.Text = "Kunde inte spara startinställningarna";
        feedbackTitle.Foreground = SoftKeyPopupDesign.ErrorText;
        status.Foreground = SoftKeyPopupDesign.ErrorText;
        status.Text = message;
        progress.Visibility = Visibility.Collapsed;
        infoStrip.Visibility = Visibility.Collapsed;
        feedbackCard.Visibility = Visibility.Visible;
    }

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
        SetBusy(true);
        status.Text = "Förbereder startinställningarna…";
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
            ShowError(ex is OperationCanceledException
                ? "Ändringen tog för lång tid. Kontrollera startinställningarna innan du försöker igen."
                : ex.Message);
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
        RefreshState();
    }

    void SetBusy(bool value)
    {
        busy = value;
        if (savedChoices != null)
            login.IsEnabled = automatic.IsEnabled = afterUnlock.IsEnabled = ok.IsEnabled = cancel.IsEnabled = !value;
        ok.Opacity = cancel.Opacity = value ? 0.5 : 1;
        RefreshApplyButton();
        if (value)
        {
            hasError = false;
            feedbackCard.Background = SoftKeyPopupDesign.CardSurface;
            feedbackCard.BorderBrush = SoftKeyPopupDesign.CardBorder;
            feedbackTitle.Text = "Sparar startinställningarna…";
            feedbackTitle.Foreground = SoftKeyPopupDesign.PrimaryText;
            status.Foreground = SoftKeyPopupDesign.SecondaryText;
            progress.Visibility = Visibility.Visible;
            progress.IsIndeterminate = true;
            progress.Value = 0;
            infoStrip.Visibility = Visibility.Collapsed;
            feedbackCard.Visibility = Visibility.Visible;
        }
        else
        {
            progress.Visibility = Visibility.Collapsed;
            progress.IsIndeterminate = false;
            progress.Value = 0;
            if (!hasError) ShowIdleNote();
        }
    }
}
