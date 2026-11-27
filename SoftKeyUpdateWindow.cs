using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

sealed class SoftKeyUpdateWindow : Window
{
    const string UpdateIconOutline = "M 10,3 V 12 M 10,12 L 6.5,8.5 M 10,12 L 13.5,8.5 M 4,15.5 H 16";
    const string CheckIconOutline = "M 5.5,10.5 L 8.5,13.5 L 14.5,7";
    const string SearchIconOutline = "M 13,13 L 16.5,16.5 M 14,9 A 5,5 0 1 1 4,9 A 5,5 0 1 1 14,9";
    const string InfoIconOutline = "M 10,6 V 10.5 M 10,13.5 V 14 M 10,2.5 A 7.5,7.5 0 1 1 9.99,2.5";

    static readonly Brush ErrorSurface = new SolidColorBrush(Color.FromRgb(0xFD, 0xF2, 0xF2));
    static readonly Brush ErrorBorder = new SolidColorBrush(Color.FromRgb(0xE6, 0xB8, 0xB8));

    readonly TextBlock title = new()
    {
        FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
    };
    readonly TextBlock metadata = new()
    {
        FontSize = 12, Foreground = SoftKeyPopupDesign.ActionText, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0)
    };
    readonly System.Windows.Shapes.Path statusGlyph = new()
    {
        Stroke = SoftKeyPopupDesign.ActionText, StrokeThickness = 1.6,
        Width = 20, Height = 20, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    readonly ProgressBar searchProgress = new()
    {
        Height = 4, Minimum = 0, Maximum = 100, IsIndeterminate = true,
        Foreground = SoftKeyPopupDesign.Accent, Background = SoftKeyPopupDesign.ActionSurface,
        BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed
    };
    readonly TextBlock notesHeading = new()
    {
        Text = "Nyheter i uppdateringen", FontSize = 12, FontWeight = FontWeights.SemiBold,
        Foreground = SoftKeyPopupDesign.ActionText, Margin = new Thickness(2, 12, 0, 6)
    };
    readonly TextBlock notes = new()
    {
        FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 20
    };
    readonly ScrollViewer notesScroll = new()
    {
        MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    readonly Border description;
    readonly StackPanel summary = new();
    readonly TextBlock feedbackText = new()
    {
        FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick
    };
    readonly Border feedbackCard;
    readonly SoftKeyUpdateProgressView progress = new();
    readonly Button check = SoftKeyButtonDesign.Action("Sök efter uppdateringar", true);
    readonly Button install = SoftKeyButtonDesign.Action("Installera uppdateringen", true);
    readonly Button close = SoftKeyButtonDesign.Action("Stäng");
    readonly Func<Task<SoftKeyUpdates.Preview>> fetch;
    readonly Func<SoftKeyUpdates.Preview, IProgress<SoftKeyUpdates.Progress>, Task> apply;
    SoftKeyUpdates.Preview available;
    bool busy;

    public SoftKeyUpdateWindow(SoftKeyUpdates.Preview initial, Func<Task<SoftKeyUpdates.Preview>> fetch,
        Func<SoftKeyUpdates.Preview, IProgress<SoftKeyUpdates.Progress>, Task> apply)
    {
        Title = "Uppdateringar";
        this.fetch = fetch;
        this.apply = apply;
        ShowInTaskbar = false;
        ShowActivated = false;
        SourceInitialized += (_, _) => Native.NoActivate(new WindowInteropHelper(this).Handle);
        AutomationProperties.SetLiveSetting(title, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(feedbackText, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(searchProgress, "Söker efter uppdateringar");

        var iconBadge = SoftKeySettingsDesign.Icon(statusGlyph);
        DockPanel.SetDock(iconBadge, Dock.Right);
        var headerText = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title, metadata }
        };
        var statusCard = new Border
        {
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(14, 12, 14, 12),
            Child = new StackPanel { Children = { new DockPanel { Children = { iconBadge, headerText } }, searchProgress } }
        };

        notesScroll.Content = notes;
        description = new Border
        {
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(14, 11, 14, 11), Child = notesScroll
        };
        summary.Children.Add(statusCard);
        summary.Children.Add(notesHeading);
        summary.Children.Add(description);

        feedbackCard = new Border
        {
            Background = ErrorSurface, BorderBrush = ErrorBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed, Child = feedbackText
        };
        progress.Content.Visibility = Visibility.Collapsed;

        close.Focusable = check.Focusable = install.Focusable = false;
        check.Width = double.NaN; check.MinWidth = 112; check.Padding = new Thickness(14, 3, 14, 3);
        install.Width = double.NaN; install.MinWidth = 112; install.Padding = new Thickness(14, 3, 14, 3);
        close.Width = 90;
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        check.Click += async (_, _) => await Check();
        install.Click += async (_, _) => await Install();

        var content = new StackPanel { Children = { summary, progress.Content, feedbackCard } };
        var sections = SoftKeyPopupDesign.Sections(SoftKeySettingsDesign.Width, content, SoftKeyButtonDesign.Row(close, check, install));
        SoftKeyPopupDesign.ConfigureWindow(this, area =>
        {
            sections.Width = Math.Clamp(area.Width - 24, 280, SoftKeySettingsDesign.Width);
            notesScroll.MaxHeight = Math.Clamp(area.Height - 238, 72, 190);
        });
        Content = SoftKeyPopupDesign.Frame(Title, sections, Close, () => { if (!busy) DragMove(); });
        SoftKeyPopupDesign.KeepResizeInWorkArea(this);
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        SetAvailable(initial);
        if (initial == null) Loaded += async (_, _) => await Check();
    }

    public void SetAvailable(SoftKeyUpdates.Preview preview)
    {
        if (busy) return;
        available = preview;
        searchProgress.Visibility = Visibility.Collapsed;
        long currentBuild = SoftKeyUpdates.CurrentBuildNumber;
        if (preview == null)
        {
            SetGlyph(CheckIconOutline);
            title.Text = "SoftKey är uppdaterat";
            metadata.Text = currentBuild > 0
                ? $"Installerad version: SoftKey {currentBuild}"
                : "Du använder den senaste tillgängliga versionen.";
            notesHeading.Visibility = description.Visibility = Visibility.Collapsed;
        }
        else
        {
            SetGlyph(UpdateIconOutline);
            title.Text = $"Ny uppdatering: SoftKey {preview.BuildNumber}";
            string size = SoftKeyUpdates.FormatSize(preview.Asset.Size);
            string published = DateTimeOffset.TryParse(preview.Published, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                ? $"Publicerad {date.LocalDateTime.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("sv-SE"))}  ·  {size}"
                : size;
            metadata.Text = currentBuild > 0 ? $"{published}  ·  Nuvarande: {currentBuild}" : published;
            notes.Text = string.IsNullOrWhiteSpace(preview.Notes)
                ? "Det finns inga versionsanteckningar för den här uppdateringen."
                : preview.Notes.Trim();
            notesHeading.Visibility = description.Visibility = Visibility.Visible;
        }
        ShowFeedback(null);
        SetButtons();
    }

    void SetGlyph(string outline) => statusGlyph.Data = Geometry.Parse(outline);

    void SetButtons()
    {
        check.Visibility = available == null ? Visibility.Visible : Visibility.Collapsed;
        install.Visibility = available != null ? Visibility.Visible : Visibility.Collapsed;
        check.IsEnabled = close.IsEnabled = !busy;
        check.Opacity = check.IsEnabled ? 1 : 0.55;
        close.Opacity = close.IsEnabled ? 1 : 0.55;
        install.IsEnabled = !busy && available != null && SoftKeyUpdates.CanInstall;
        install.Opacity = install.IsEnabled ? 1 : 0.55;
        install.ToolTip = SoftKeyUpdates.CanInstall ? null : "Öppna den installerade SoftKey för att installera uppdateringen.";
    }

    void ShowFeedback(string text)
    {
        feedbackText.Text = text;
        feedbackCard.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    async Task Check()
    {
        busy = true;
        SetButtons();
        SetGlyph(SearchIconOutline);
        title.Text = "Söker efter uppdateringar…";
        metadata.Text = "Kontrollerar publicerade versioner";
        searchProgress.Visibility = Visibility.Visible;
        notesHeading.Visibility = description.Visibility = Visibility.Collapsed;
        ShowFeedback(null);
        try
        {
            var result = await fetch();
            busy = false;
            SetAvailable(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            SetGlyph(InfoIconOutline);
            searchProgress.Visibility = Visibility.Collapsed;
            title.Text = "Sökningen kunde inte slutföras";
            metadata.Text = "Det gick inte att hämta versionsinformation just nu.";
            ShowFeedback("Kontrollera internetanslutningen och försök igen.");
        }
        finally
        {
            busy = false;
            SetButtons();
        }
    }

    async Task Install()
    {
        if (!install.IsEnabled) return;
        busy = true;
        SetButtons();
        ShowFeedback(null);
        summary.Visibility = Visibility.Collapsed;
        progress.Content.Visibility = Visibility.Visible;
        progress.Show(new SoftKeyUpdates.Progress(0, "Förbereder hämtningen", available.Asset.Name));
        var status = new Progress<SoftKeyUpdates.Progress>(progress.Show);
        try
        {
            await apply(available, status);
            busy = false;
            Close();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or
            InvalidOperationException or CryptographicException or JsonException or OperationCanceledException)
        {
            ShowFeedback(ex switch
            {
                Win32Exception { NativeErrorCode: 1223 } => "Installationen avbröts när Windows bad om godkännande.",
                IOException => ex.Message,
                _ => "Uppdateringen kunde inte slutföras. Försök igen."
            });
            summary.Visibility = Visibility.Visible;
            progress.Content.Visibility = Visibility.Collapsed;
        }
        finally
        {
            busy = false;
            SetButtons();
        }
    }
}
