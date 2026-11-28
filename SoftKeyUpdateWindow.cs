using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
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
    const string DownloadArrowOutline = "M 10,3.5 V 13 M 10,13 L 6.5,9.5 M 10,13 L 13.5,9.5 M 4.5,16.5 H 15.5";
    const string DownloadSparkleOutline = "M 15.8,3.2 V 5.8 M 14.5,4.5 H 17.1 M 14.3,7.3 V 8.7 M 13.6,8 H 15";
    const string CheckIconOutline = "M 5.5,10.5 L 8.5,13.5 L 14.5,7";
    const string SearchIconOutline = "M 13,13 L 16.5,16.5 M 14,9 A 5,5 0 1 1 4,9 A 5,5 0 1 1 14,9";
    const string InfoIconOutline = "M 10,6 V 10.5 M 10,13.5 V 14 M 10,2.5 A 7.5,7.5 0 1 1 9.99,2.5";
    const string CalendarIconOutline = "M 2.5,4.5 H 11.5 A 1,1 0 0 1 12.5,5.5 V 11.5 A 1,1 0 0 1 11.5,12.5 H 2.5 A 1,1 0 0 1 1.5,11.5 V 5.5 A 1,1 0 0 1 2.5,4.5 Z M 1.5,7 H 12.5 M 4.5,2.5 V 4.5 M 9.5,2.5 V 4.5";
    const string PackageIconOutline = "M 7,1.8 L 12.2,4.5 V 9.8 L 7,12.5 L 1.8,9.8 V 4.5 Z M 1.8,4.5 L 7,7.2 L 12.2,4.5 M 7,7.2 V 12.5";
    const string ShieldCheckOutline = "M 7,1.5 L 11.8,3.4 V 6.9 C 11.8,9.7 9.7,11.7 7,12.6 C 4.3,11.7 2.2,9.7 2.2,6.9 V 3.4 Z M 4.9,7.1 L 6.4,8.6 L 9.4,5.6";
    const string ShieldInfoOutline = "M 7.5,1.5 L 12.8,3.6 V 7.4 C 12.8,10.5 10.5,12.7 7.5,13.7 C 4.5,12.7 2.2,10.5 2.2,7.4 V 3.6 Z M 7.5,5.3 V 5.5 M 7.5,7.3 V 10.4";
    const string ButtonDownloadOutline = "M 6,1.5 V 8.2 M 6,8.2 L 3.3,5.5 M 6,8.2 L 8.7,5.5 M 2,10.5 H 10";

    readonly TextBlock title = new()
    {
        FontSize = 15.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    readonly TextBlock versionBadgeText = new()
    {
        FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = SoftKeyPopupDesign.ActionText
    };
    readonly Border versionBadge;
    readonly TextBlock metadata = new()
    {
        FontSize = 12, Foreground = SoftKeyPopupDesign.SecondaryText, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0)
    };
    readonly WrapPanel metaStrip = new()
    {
        Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed
    };
    readonly TextBlock publishedMeta = new() { FontSize = 12, Foreground = SoftKeyPopupDesign.SecondaryText, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock sizeMeta = new() { FontSize = 12, Foreground = SoftKeyPopupDesign.SecondaryText, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Windows.Shapes.Path statusGlyph = new()
    {
        Stroke = SoftKeyPopupDesign.Accent, StrokeThickness = 1.7,
        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };
    readonly System.Windows.Shapes.Path sparkleGlyph = new()
    {
        Data = Geometry.Parse(DownloadSparkleOutline),
        Stroke = SoftKeyPopupDesign.Accent, StrokeThickness = 1.2, Opacity = 0.78,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        Visibility = Visibility.Collapsed
    };
    readonly ProgressBar searchProgress = new()
    {
        Height = 4, Minimum = 0, Maximum = 100, IsIndeterminate = true,
        Foreground = SoftKeyPopupDesign.Accent, Background = SoftKeyPopupDesign.ActionSurface,
        Template = SoftKeyPopupDesign.ProgressTemplate,
        BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed
    };
    readonly TextBlock notesHeading = new()
    {
        Text = "Vad som är nytt", FontSize = 13, FontWeight = FontWeights.SemiBold,
        Foreground = SoftKeyPopupDesign.PrimaryText, VerticalAlignment = VerticalAlignment.Center
    };
    readonly TextBlock changeCountText = new()
    {
        FontSize = 11.5, FontWeight = FontWeights.Medium, Foreground = SoftKeyPopupDesign.SecondaryText
    };
    readonly Border changeCountBadge;
    readonly DockPanel notesHeaderRow;
    readonly StackPanel notesList = new();
    readonly ScrollViewer notesScroll = new()
    {
        MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    readonly Border description;
    readonly Border reassuranceBanner;
    readonly StackPanel summary = new();
    readonly TextBlock feedbackText = new()
    {
        FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = SoftKeyPopupDesign.ErrorText
    };
    readonly Border feedbackCard;
    readonly SoftKeyUpdateProgressView progress = new();
    readonly TextBlock footerVersion = new()
    {
        FontSize = 11.5, Foreground = SoftKeyPopupDesign.SecondaryText, VerticalAlignment = VerticalAlignment.Center
    };
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

        versionBadge = new Border
        {
            Background = SoftKeyPopupDesign.ActionSurface, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            Child = versionBadgeText
        };

        var iconCanvas = new Canvas { Width = 20, Height = 20, Children = { statusGlyph, sparkleGlyph } };
        var iconTile = CreateHeroTile(new Viewbox { Width = 22, Height = 22, Stretch = Stretch.Uniform, Child = iconCanvas });
        DockPanel.SetDock(iconTile, Dock.Left);

        var titleRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(title);
        titleRow.Children.Add(versionBadge);

        BuildMetaStrip();

        var headerText = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleRow, metadata, metaStrip }
        };
        var statusCard = new Border
        {
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(14, 12, 14, 12),
            Child = new StackPanel { Children = { new DockPanel { Children = { iconTile, headerText } }, searchProgress } }
        };

        changeCountBadge = new Border
        {
            Background = SoftKeyPopupDesign.DialogFooter, CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed, Child = changeCountText
        };
        DockPanel.SetDock(changeCountBadge, Dock.Right);
        notesHeaderRow = new DockPanel { Margin = new Thickness(2, 13, 2, 7) };
        notesHeaderRow.Children.Add(changeCountBadge);
        notesHeaderRow.Children.Add(notesHeading);

        notesScroll.Content = notesList;
        description = new Border
        {
            Background = SoftKeyPopupDesign.CardSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(14, 12, 14, 12), Child = notesScroll
        };

        reassuranceBanner = CreateReassuranceBanner();

        summary.Children.Add(statusCard);
        summary.Children.Add(notesHeaderRow);
        summary.Children.Add(description);
        summary.Children.Add(reassuranceBanner);

        feedbackCard = new Border
        {
            Background = SoftKeyPopupDesign.ErrorSurface, BorderBrush = SoftKeyPopupDesign.ErrorBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed, Child = feedbackText
        };
        progress.Content.Visibility = Visibility.Collapsed;

        close.Focusable = check.Focusable = install.Focusable = false;
        check.Width = double.NaN; check.MinWidth = 112; check.Padding = new Thickness(14, 3, 14, 3);
        install.Width = double.NaN; install.MinWidth = 112; install.Padding = new Thickness(13, 3, 14, 3);
        install.FontWeight = FontWeights.SemiBold;
        install.Content = CreateInstallButtonContent();
        AutomationProperties.SetName(install, "Installera uppdateringen");
        close.Width = 86;
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        check.Click += async (_, _) => await Check();
        install.Click += async (_, _) => await Install();

        var actions = SoftKeyButtonDesign.Row(close, check, install);
        DockPanel.SetDock(actions, Dock.Right);
        var footer = new DockPanel { LastChildFill = true };
        footer.Children.Add(actions);
        footer.Children.Add(footerVersion);

        var content = new StackPanel { Children = { summary, progress.Content, feedbackCard } };
        var sections = SoftKeyPopupDesign.Sections(SoftKeySettingsDesign.Width, content, footer);
        SoftKeyPopupDesign.ConfigureWindow(this, area =>
        {
            sections.Width = Math.Clamp(area.Width - 24, 300, SoftKeySettingsDesign.Width);
            notesScroll.MaxHeight = Math.Clamp(area.Height - 310, 84, 220);
        });
        Content = SoftKeyPopupDesign.Frame(Title, sections, Close, () => { if (!busy) DragMove(); });
        SoftKeyPopupDesign.KeepResizeInWorkArea(this);
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        SetAvailable(initial);
        if (initial == null) Loaded += async (_, _) => await Check();
    }

    internal static Border CreateHeroTile(UIElement glyph) => new()
    {
        Width = 42, Height = 42, CornerRadius = new CornerRadius(9),
        Margin = new Thickness(0, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center,
        Background = SoftKeyPopupDesign.ActionSurface, Child = glyph, IsHitTestVisible = false
    };

    void BuildMetaStrip()
    {
        var dateGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        dateGroup.Children.Add(MiniIcon(CalendarIconOutline, 13, SoftKeyPopupDesign.SecondaryText, new Thickness(0, 0, 4, 0)));
        dateGroup.Children.Add(publishedMeta);

        var sizeGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sizeGroup.Children.Add(MiniIcon(PackageIconOutline, 13, SoftKeyPopupDesign.SecondaryText, new Thickness(0, 0, 4, 0)));
        sizeGroup.Children.Add(sizeMeta);

        var verifiedContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        verifiedContent.Children.Add(MiniIcon(ShieldCheckOutline, 12.5, SoftKeyPopupDesign.ActionText, new Thickness(0, 0, 4, 0)));
        verifiedContent.Children.Add(new TextBlock
        {
            Text = "Verifierad signatur", FontSize = 11.5, FontWeight = FontWeights.Medium,
            Foreground = SoftKeyPopupDesign.ActionText, VerticalAlignment = VerticalAlignment.Center
        });
        var verifiedPill = new Border
        {
            Background = SoftKeyPopupDesign.DialogFooter, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1.5, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = verifiedContent
        };

        metaStrip.Children.Add(dateGroup);
        metaStrip.Children.Add(MetaSeparator());
        metaStrip.Children.Add(sizeGroup);
        metaStrip.Children.Add(MetaSeparator());
        metaStrip.Children.Add(verifiedPill);
    }

    static TextBlock MetaSeparator() => new()
    {
        Text = "·", FontSize = 12, Foreground = SoftKeyPopupDesign.SecondaryText,
        Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
    };

    static System.Windows.Shapes.Path MiniIcon(string outline, double size, Brush stroke, Thickness margin) => new()
    {
        Data = Geometry.Parse(outline), Stroke = stroke, StrokeThickness = 1.35,
        Width = size, Height = size, Stretch = Stretch.Uniform,
        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        VerticalAlignment = VerticalAlignment.Center, Margin = margin, IsHitTestVisible = false
    };

    static Border CreateReassuranceBanner()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = MiniIcon(ShieldInfoOutline, 17, SoftKeyPopupDesign.Accent, new Thickness(0, 1, 10, 0));
        icon.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(icon);

        var message = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "Inställningar, sparade ord och inloggningsläge bevaras.",
                    FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = SoftKeyPopupDesign.PrimaryText,
                    TextWrapping = TextWrapping.Wrap, LineHeight = 18
                },
                new TextBlock
                {
                    Text = "SoftKey startar om automatiskt efter installationen.",
                    FontSize = 12.5, Foreground = SoftKeyPopupDesign.SecondaryText,
                    TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 3, 0, 0)
                }
            }
        };
        Grid.SetColumn(message, 1);
        grid.Children.Add(message);

        return new Border
        {
            Background = SoftKeyKeyDesign.ModifierFace, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(SoftKeyPopupDesign.CardCornerRadius),
            Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed, Child = grid
        };
    }

    static StackPanel CreateInstallButtonContent() => new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            MiniIcon(ButtonDownloadOutline, 12, SoftKeyPopupDesign.OnAccentText, new Thickness(0, 0, 6, 0)),
            new TextBlock { Text = "Installera uppdateringen", VerticalAlignment = VerticalAlignment.Center }
        }
    };

    public void SetAvailable(SoftKeyUpdates.Preview preview)
    {
        if (busy) return;
        available = preview;
        searchProgress.Visibility = Visibility.Collapsed;
        long currentBuild = SoftKeyUpdates.CurrentBuildNumber;
        UpdateFooterVersion(currentBuild);
        if (preview == null)
        {
            SetGlyph(CheckIconOutline, showSparkle: false);
            title.Text = "SoftKey är uppdaterat";
            versionBadge.Visibility = Visibility.Collapsed;
            metaStrip.Visibility = Visibility.Collapsed;
            metadata.Visibility = Visibility.Visible;
            metadata.Text = currentBuild > 0
                ? $"Installerad version: SoftKey {currentBuild}  ·  Du använder den senaste versionen."
                : "Du använder den senaste tillgängliga versionen.";
            notesHeaderRow.Visibility = description.Visibility = reassuranceBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            SetGlyph(DownloadArrowOutline, showSparkle: true);
            title.Text = $"SoftKey {preview.BuildNumber} är redo";
            versionBadgeText.Text = currentBuild > 0 ? $"v{currentBuild} → v{preview.BuildNumber}" : $"v{preview.BuildNumber}";
            versionBadge.Visibility = Visibility.Visible;

            publishedMeta.Text = DateTimeOffset.TryParse(preview.Published, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                ? date.LocalDateTime.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("sv-SE"))
                : "Ny version";
            sizeMeta.Text = SoftKeyUpdates.FormatSize(preview.Asset.Size);
            metadata.Visibility = Visibility.Collapsed;
            metaStrip.Visibility = Visibility.Visible;

            PopulateNotes(preview.Notes);
            notesHeaderRow.Visibility = description.Visibility = reassuranceBanner.Visibility = Visibility.Visible;
        }
        ShowFeedback(null);
        SetButtons();
    }

    void UpdateFooterVersion(long currentBuild)
    {
        footerVersion.Text = currentBuild > 0 ? $"Installerad: SoftKey {currentBuild}" : "";
        footerVersion.Visibility = currentBuild > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void PopulateNotes(string rawNotes)
    {
        notesList.Children.Clear();
        var parsed = SoftKeyReleaseNotes.Parse(rawNotes);
        if (parsed.Entries.Count == 0)
        {
            changeCountBadge.Visibility = Visibility.Collapsed;
            notesList.Children.Add(new TextBlock
            {
                Text = "Det finns inga versionsanteckningar för den här uppdateringen.",
                FontSize = 12.5, Foreground = SoftKeyPopupDesign.SecondaryText, TextWrapping = TextWrapping.Wrap, LineHeight = 18
            });
            return;
        }

        changeCountText.Text = parsed.Entries.Count == 1 ? "1 ändring" : $"{parsed.Entries.Count} ändringar";
        changeCountBadge.Visibility = Visibility.Visible;

        if (!string.IsNullOrEmpty(parsed.Summary))
        {
            notesList.Children.Add(new TextBlock
            {
                Text = parsed.Summary, FontSize = 12.5, Foreground = SoftKeyPopupDesign.SecondaryText,
                TextWrapping = TextWrapping.Wrap, LineHeight = 18
            });
            notesList.Children.Add(new Border
            {
                Height = 1, Background = SoftKeyPopupDesign.CardBorder,
                Margin = new Thickness(0, 10, 0, 10), IsHitTestVisible = false
            });
        }

        for (int i = 0; i < parsed.Entries.Count; i++)
        {
            var entry = parsed.Entries[i];
            var row = new Grid { Margin = i == 0 ? new Thickness(0) : new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tag = CreateCategoryTag(entry.Category);
            row.Children.Add(tag);

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = entry.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                Foreground = SoftKeyPopupDesign.PrimaryText, TextWrapping = TextWrapping.Wrap, LineHeight = 17.5
            });
            if (!string.IsNullOrEmpty(entry.Detail))
            {
                text.Children.Add(new TextBlock
                {
                    Text = entry.Detail, FontSize = 11.5, Foreground = SoftKeyPopupDesign.SecondaryText,
                    TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(0, 1.5, 0, 0)
                });
            }
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            notesList.Children.Add(row);
        }
        notesScroll.ScrollToTop();
    }

    static Border CreateCategoryTag(SoftKeyReleaseNotes.Category category)
    {
        (Brush background, Brush border, Brush foreground) = category switch
        {
            SoftKeyReleaseNotes.Category.Nyhet => (SoftKeyPopupDesign.ActionSurface, Brushes.Transparent, SoftKeyPopupDesign.ActionText),
            SoftKeyReleaseNotes.Category.Fix => (SoftKeyPopupDesign.DialogSurface, SoftKeyPopupDesign.CardBorder, SoftKeyPopupDesign.SecondaryText),
            _ => (SoftKeyPopupDesign.DialogFooter, Brushes.Transparent, SoftKeyPopupDesign.SecondaryText)
        };
        return new Border
        {
            Background = background, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2.5, 7, 2.5),
            Margin = new Thickness(0, 1, 10, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = SoftKeyReleaseNotes.Label(category), FontSize = 9.5,
                FontWeight = FontWeights.SemiBold, Foreground = foreground
            }
        };
    }

    void SetGlyph(string outline, bool showSparkle)
    {
        statusGlyph.Data = Geometry.Parse(outline);
        statusGlyph.Stroke = showSparkle ? SoftKeyPopupDesign.Accent : SoftKeyPopupDesign.ActionText;
        sparkleGlyph.Visibility = showSparkle ? Visibility.Visible : Visibility.Collapsed;
    }

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
        SetGlyph(SearchIconOutline, showSparkle: false);
        title.Text = "Söker efter uppdateringar…";
        versionBadge.Visibility = Visibility.Collapsed;
        metaStrip.Visibility = Visibility.Collapsed;
        metadata.Visibility = Visibility.Visible;
        metadata.Text = "Kontrollerar publicerade versioner";
        searchProgress.Visibility = Visibility.Visible;
        notesHeaderRow.Visibility = description.Visibility = reassuranceBanner.Visibility = Visibility.Collapsed;
        ShowFeedback(null);
        try
        {
            var result = await fetch();
            busy = false;
            SetAvailable(result);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            SetGlyph(InfoIconOutline, showSparkle: false);
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
        catch (Exception ex) when (ex is Win32Exception or IOException or HttpRequestException or UnauthorizedAccessException or
            InvalidOperationException or CryptographicException or JsonException or OperationCanceledException)
        {
            ShowFeedback(ex switch
            {
                Win32Exception { NativeErrorCode: 1223 } => "Installationen avbröts när Windows bad om godkännande.",
                IOException => ex.Message,
                HttpRequestException => "Uppdateringen kunde inte hämtas. Kontrollera internetanslutningen och försök igen.",
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
