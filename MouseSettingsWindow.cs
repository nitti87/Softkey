using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Collections.Generic;
using System.Windows.Shapes;
using System.Linq;

// Skrivbordets inställningar för viloklick.
// Siffrorna kan skrivas med SoftKey utan att fönstret behöver ha fokus (se TryType).
class MouseSettingsWindow : Window
{
    const string PreviewPrompt = "Vila här";
    const string PreviewInvalid = "Justera värdena";
    const string SignInHelp = "Den här inställningen sparas också för Windows-inloggningen.";
    static readonly ControlTemplate HelpTipTemplate = new(typeof(ToolTip))
    {
        VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
    };
    static readonly ControlTemplate TimingInputTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type TextBox}">
            <Border CornerRadius="6" Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                <ScrollViewer x:Name="PART_ContentHost" Focusable="False"/>
            </Border>
        </ControlTemplate>
        """);
    static readonly Point RingCenter = new(26, 26);
    const double RingRadius = 23;
    readonly Settings cfg;
    readonly Func<bool> save;
    readonly Func<AutoclickTimingSettings, Task> shareTiming;
    bool isSaving;
    readonly TextBox dwell = new(), jitter = new();
    readonly Border dwellSignInHelp, jitterSignInHelp;
    readonly TextBlock dwellError = ErrorMessage(), jitterError = ErrorMessage(), saveError = ErrorMessage();
    readonly Border preview = new()
    {
        Background = SoftKeyPopupDesign.DialogSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(6, 8, 6, 4), Height = 98, MinWidth = 90, VerticalAlignment = VerticalAlignment.Center
    };
    readonly TextBlock previewStatus = new()
    {
        FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = SoftKeyPopupDesign.SecondaryText,
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0)
    };
    readonly Path previewArc = new()
    {
        Data = Geometry.Empty, Stroke = SoftKeyPopupDesign.Accent, StrokeThickness = 3.4,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };
    readonly Viewbox previewPointer = DragKeyButton.CreateSymbol(DragKeySymbol.AutoClick);
    readonly Path previewCheck = new()
    {
        Data = Geometry.Parse("M 6,12 L 10,16 L 18,8"), Stroke = SoftKeyPopupDesign.Accent, StrokeThickness = 2.6,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Hidden
    };
    readonly DwellTracker previewTracker = new();
    int previewDelay, previewTolerance;
    bool pointerInPreview;
    double previewProgress;
    readonly HashSet<KeyDef> chordModifiers = new();
    public event Action KeyAvailabilityChanged;
    readonly SoftKeyCheckBox autoClick = SoftKeySettingsDesign.CheckOption("Aktivera autoklick",
        "Klicka när pekaren vilar den tid du har valt.", SoftKeySettingsDesign.Icon(DragKeyButton.CreateSymbol(DragKeySymbol.AutoClick)));
    readonly SoftKeyCheckBox repeat = SoftKeySettingsDesign.CheckOption("Upprepa klick",
        "Fortsätt klicka medan pekaren står stilla.",
        SoftKeySettingsDesign.Icon("M 3,9 A 7,7 0 0 1 15,5 L 17,7 M 17,3 V 7 H 13 M 17,11 A 7,7 0 0 1 5,15 L 3,13 M 3,17 V 13 H 7"));
    readonly Button apply = SoftKeyButtonDesign.Apply(primary: true);
    bool signInTimingPending;
    readonly Button cancel = SoftKeyButtonDesign.Action("Avbryt");
    TextBox activeBox;   // rutan som SoftKey skriver i just nu (null = ingen)
    readonly Dictionary<TextBox, Rectangle> carets = new();   // vår egen markör för varje ruta
    internal bool IsPointerOverSignInHelp => dwellSignInHelp?.IsMouseOver == true || jitterSignInHelp?.IsMouseOver == true;

    public MouseSettingsWindow(Settings cfg, Func<bool> save, Func<AutoclickTimingSettings, Task> shareTiming = null)
    {
        this.cfg = cfg;
        this.save = save;
        this.shareTiming = shareTiming;
        Title = "Autoklick";
        ShowActivated = false;
        SourceInitialized += (_, _) => Native.NoActivate(new WindowInteropHelper(this).Handle);
        Topmost = cfg.AlwaysOnTop;
        ShowInTaskbar = false;

        // Klick på något annat än en talruta avslutar "skriv i rutan"-läget.
        PreviewMouseLeftButtonDown += (_, e) => { if (e.Source is not TextBox && !IsPointerOverSignInHelp) Select(null); };

        var options = new StackPanel { Children = { SoftKeySettingsDesign.Heading("Klickbeteende") } };
        autoClick.Margin = new Thickness(0, 0, 0, 8);
        options.Children.Add(autoClick);
        options.Children.Add(repeat);
        options.Children.Add(SoftKeyPopupDesign.Separator(new Thickness(1, 14, 1, 13)));
        options.Children.Add(SoftKeySettingsDesign.Heading("Tid och rörelse"));
        if (shareTiming != null)
        {
            dwellSignInHelp = SignInHelpIcon("Tid före klick");
            jitterSignInHelp = SignInHelpIcon("Rörelsetolerans");
        }
        var timing = new StackPanel();
        timing.Children.Add(Row("Tid före klick", dwell, "ms  (200–5000)", dwellError, dwellSignInHelp));
        const string toleranceHelp = "Hur långt pekaren får röra sig under väntetiden.";
        timing.Children.Add(Row("Rörelsetolerans", jitter, "px  (2–100)", jitterError, jitterSignInHelp, toleranceHelp));
        var timingAndPreview = new Grid();
        timingAndPreview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timingAndPreview.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timingAndPreview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        timingAndPreview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        timingAndPreview.Children.Add(timing);
        Grid.SetColumn(preview, 1);
        timingAndPreview.Children.Add(BuildPreview());
        timingAndPreview.SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged) return;
            bool stack = e.NewSize.Width < 355;
            Grid.SetColumnSpan(timing, stack ? 2 : 1);
            Grid.SetColumn(preview, stack ? 0 : 1);
            Grid.SetColumnSpan(preview, stack ? 2 : 1);
            Grid.SetRow(preview, stack ? 1 : 0);
            preview.HorizontalAlignment = stack ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        };
        options.Children.Add(SoftKeySettingsDesign.Card(timingAndPreview));
        saveError.Margin = new Thickness(10, 2, 10, 2);
        AutomationProperties.SetLiveSetting(saveError, AutomationLiveSetting.Polite);
        options.Children.Add(saveError);

        jitter.MaxLength = 3; dwell.MaxLength = 4;
        jitter.TextChanged += TimingEdited;
        dwell.TextChanged += TimingEdited;
        autoClick.Checked += (_, _) => RefreshApplyButton();
        autoClick.Unchecked += (_, _) => RefreshApplyButton();
        repeat.Checked += (_, _) => RefreshApplyButton();
        repeat.Unchecked += (_, _) => RefreshApplyButton();

        apply.IsDefault = cancel.IsCancel = true;
        apply.Click += async (_, _) => await ApplyAndCloseAsync();
        cancel.Click += (_, _) => Close();
        Closing += (_, e) => { if (isSaving && Owner?.IsVisible == true) e.Cancel = true; };
        var scroll = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = SoftKeyPopupDesign.Sections(SoftKeySettingsDesign.Width, scroll, SoftKeyButtonDesign.Row(cancel, apply));
        Content = SoftKeyPopupDesign.Frame("Autoklick", panel, Close, () => { if (!isSaving) DragMove(); });
        SoftKeySettingsDesign.FitDialog(this, panel, scroll, 132, cancel, apply);
        Reload();
        Loaded += (_, _) => { Select(dwell); dwell.SelectAll(); };
    }

    void TimingEdited(object sender, TextChangedEventArgs e)
    {
        (sender == dwell ? dwellError : jitterError).Visibility = Visibility.Collapsed;
        ReadPreviewSettings();
        RefreshApplyButton();
    }

    Border BuildPreview()
    {
        var content = new StackPanel { IsHitTestVisible = false };
        var target = new Grid { Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Center };
        target.Children.Add(new Ellipse
        {
            Width = 46, Height = 46, Fill = SoftKeyPopupDesign.CardSurface,
            Stroke = SoftKeyPopupDesign.CardBorder, StrokeThickness = 2
        });
        previewPointer.HorizontalAlignment = HorizontalAlignment.Center;
        target.Children.Add(previewArc);
        target.Children.Add(previewPointer);
        target.Children.Add(previewCheck);
        content.Children.Add(target);
        AutomationProperties.SetName(preview, "Prova Autoklick");
        AutomationProperties.SetHelpText(preview, "Vila här för att prova väntetiden och rörelsetoleransen.");
        AutomationProperties.SetLiveSetting(previewStatus, AutomationLiveSetting.Polite);
        content.Children.Add(previewStatus);
        preview.Child = content;
        return preview;
    }

    void ReadPreviewSettings()
    {
        bool validDelay = TrySettingValue(dwell, Settings.MinDwellMs, Settings.MaxDwellMs, out int delay);
        bool validTolerance = TrySettingValue(jitter, Settings.MinJitterPx, Settings.MaxJitterPx, out previewTolerance);
        previewDelay = validDelay && validTolerance ? delay : 0;
        ResetDwellPreview();
    }

    internal bool UpdateDwellPreview(Native.POINT point, double now)
    {
        previewTracker.ObserveMovement(point.X, point.Y, previewTolerance, now);
        if (PresentationSource.FromVisual(preview) is not HwndSource source || !preview.IsVisible
            || !Native.IsWindowAt(point, source.Handle)
            || !new Rect(preview.RenderSize).Contains(preview.PointFromScreen(new Point(point.X, point.Y))))
        {
            if (pointerInPreview) ResetDwellPreview();
            return false;
        }
        pointerInPreview = true;
        if (previewDelay == 0) return true;
        if (previewTracker.Update(point.X, point.Y, DwellTarget.SoftKeyControl, now, previewDelay, previewTolerance, false))
            previewTracker.Complete(now, allowRepeat: false);
        ShowPreviewProgress(previewTracker.Progress(now, previewDelay));
        return true;
    }

    internal void ResetDwellPreview()
    {
        previewTracker.Reset();
        pointerInPreview = false;
        ShowPreviewProgress(0);
    }

    void ShowPreviewProgress(double progress)
    {
        bool clicked = progress == 1;
        if (previewProgress != progress)
        {
            previewProgress = progress;
            previewArc.Data = DrawRing(progress);
            previewPointer.Visibility = clicked ? Visibility.Hidden : Visibility.Visible;
            previewCheck.Visibility = clicked ? Visibility.Visible : Visibility.Hidden;
        }
        string status = previewDelay == 0 ? PreviewInvalid : clicked ? "Provklick" : PreviewPrompt;
        if (previewStatus.Text != status) previewStatus.Text = status;
    }

    static Geometry DrawRing(double progress)
    {
        if (progress == 0) return Geometry.Empty;
        if (progress == 1) return new EllipseGeometry(RingCenter, RingRadius, RingRadius);
        double angle = progress * Math.Tau;
        var end = new Point(RingCenter.X + RingRadius * Math.Sin(angle), RingCenter.Y - RingRadius * Math.Cos(angle));
        var ring = new StreamGeometry();
        using (var geometry = ring.Open())
        {
            geometry.BeginFigure(new Point(RingCenter.X, RingCenter.Y - RingRadius), false, false);
            geometry.ArcTo(end, new Size(RingRadius, RingRadius), 0, progress > 0.5, SweepDirection.Clockwise, true, false);
        }
        ring.Freeze();
        return ring;
    }

    static TextBlock ErrorMessage()
    {
        var message = new TextBlock { FontSize = 12, Foreground = SoftKeyPopupDesign.ErrorText,
            Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        return message;
    }

    static Border SignInHelpIcon(string label)
    {
        var glyph = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = SoftKeyPopupDesign.ActionText, BorderThickness = new Thickness(1), IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "?", Foreground = SoftKeyPopupDesign.ActionText, FontSize = 11, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        var icon = new Border
        {
            Width = 24, Height = 24, Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, Child = glyph,
            ToolTip = new ToolTip
            {
                Template = HelpTipTemplate, IsHitTestVisible = false, Placement = PlacementMode.Top, HorizontalOffset = -6,
                Content = new Border
                {
                    Background = SoftKeyPopupDesign.ActionText, CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(6), Padding = new Thickness(12, 8, 12, 8),
                    Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.26, Direction = 270 },
                    Child = new TextBlock
                    {
                        Text = SignInHelp, MaxWidth = 210, TextWrapping = TextWrapping.Wrap,
                        Foreground = SoftKeyPopupDesign.OnAccentText, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, LineHeight = 17
                    }
                }
            }
        };
        ToolTipService.SetInitialShowDelay(icon, 250);
        ToolTipService.SetShowDuration(icon, 20000);
        AutomationProperties.SetName(icon, $"Hjälp om {label.ToLowerInvariant()}");
        AutomationProperties.SetHelpText(icon, SignInHelp);
        icon.MouseEnter += (_, _) => SoftKeyHoverTransition.Apply(icon, Border.BackgroundProperty, SoftKeyPopupDesign.ActionSurface);
        icon.MouseLeave += (_, _) => SoftKeyHoverTransition.Apply(icon, Border.BackgroundProperty, Brushes.Transparent);
        return icon;
    }

    UIElement Row(string label, TextBox box, string unit, TextBlock error, Border signInHelp, string help = null)
    {
        box.Width = 62; box.Height = 30; box.FontSize = 13; box.MaxLength = 4; box.TextAlignment = TextAlignment.Right;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.Padding = new Thickness(6, 1, 6, 1);
        box.Template = TimingInputTemplate;
        box.BorderBrush = SoftKeyPopupDesign.ControlOutline;
        AutomationProperties.SetName(box, label);
        string fieldHelp = help == null ? unit : help + " " + unit;
        AutomationProperties.SetHelpText(box, signInHelp == null ? fieldHelp : fieldHelp + ". " + SignInHelp);
        box.CaretBrush = Brushes.Transparent;                    // gömmer Windows markör, vi ritar vår egen
        box.PreviewMouseLeftButtonDown += (_, _) => Select(box);
        box.SelectionChanged += (_, _) => UpdateCaret();         // markören flyttades (klick, pil, skrivning)
        box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);   // blockerar allt utom siffror
        DataObject.AddPastingHandler(box, (_, e) => e.CancelCommand());            // blockerar inklistring
        box.TextChanged += (_, _) =>
        {
            error.Visibility = saveError.Visibility = Visibility.Collapsed;
        };

        var caret = new Rectangle { Width = 2, Fill = SoftKeyPopupDesign.PrimaryText, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        carets[box] = caret;
        var overlay = new Canvas { IsHitTestVisible = false };   // ett genomskinligt lager ovanpå rutan
        overlay.Children.Add(caret);
        var holder = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        holder.Children.Add(box);
        holder.Children.Add(overlay);

        var grid = new Grid { Margin = new Thickness(0, 0, 8, 8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(signInHelp == null ? 105 : 122) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelText = new TextBlock
        {
            Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        };
        if (signInHelp != null) labelText.Margin = new Thickness(0, 0, signInHelp.Width + signInHelp.Margin.Right, 0);
        grid.Children.Add(labelText);
        if (signInHelp != null) grid.Children.Add(signInHelp);
        Grid.SetColumn(holder, 1); grid.Children.Add(holder);
        var unitText = new TextBlock
        {
            Text = unit, FontSize = 11, Foreground = SoftKeyPopupDesign.SecondaryText,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(unitText, 2); grid.Children.Add(unitText);
        if (help != null)
        {
            var explanation = new TextBlock { Text = help, FontSize = 12, Foreground = SoftKeyPopupDesign.SecondaryText,
                Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(explanation, 1); Grid.SetColumnSpan(explanation, 3); grid.Children.Add(explanation);
        }
        Grid.SetRow(error, 2); Grid.SetColumnSpan(error, 3); grid.Children.Add(error);
        return grid;
    }

    // Markera fältet som tar emot inmatning från SoftKey.
    void Select(TextBox t)
    {
        activeBox = t;
        dwell.Background = t == dwell ? SoftKeyPopupDesign.FieldSelected : SoftKeyPopupDesign.CardSurface;
        jitter.Background = t == jitter ? SoftKeyPopupDesign.FieldSelected : SoftKeyPopupDesign.CardSurface;
        dwell.BorderBrush = t == dwell ? SoftKeyPopupDesign.Accent : SoftKeyPopupDesign.ControlOutline;
        jitter.BorderBrush = t == jitter ? SoftKeyPopupDesign.Accent : SoftKeyPopupDesign.ControlOutline;
        UpdateCaret();
        KeyAvailabilityChanged?.Invoke();
    }

    // Ritar markören på rätt plats i den valda rutan, och döljer den i de andra.
    void UpdateCaret()
    {
        foreach (var (box, caret) in carets)
        {
            if (box != activeBox) { caret.Visibility = Visibility.Collapsed; continue; }
            box.UpdateLayout();
            var r = box.GetRectFromCharacterIndex(box.CaretIndex);
            if (r.IsEmpty) r = new Rect(box.ActualWidth - 6, 4, 0, box.ActualHeight - 8);   // tom ruta
            Canvas.SetLeft(caret, r.Left - 1);
            Canvas.SetTop(caret, r.Top + 2);
            caret.Height = Math.Max(4, r.Height - 4);
            caret.Visibility = Visibility.Visible;
        }
    }

    // Anropas från SoftKey innan en tangent skickas till Windows. true = tangenten är hanterad här.
    // Rutan redigeras som en vanlig textruta: på textmarkörens plats, alltså där du klickade.
    public bool IsChordModifierSelected(KeyDef key) => chordModifiers.Contains(key);

    public bool CanUseKey(KeyDef k)
    {
        if (isSaving) return false;
        if (k.Label is "Ctl" or "Alt" or "PrtScr" or "Enter" or "Esc" || SecureAttention.IsChord(k, chordModifiers)) return true;
        return activeBox != null && (k.Label is "BkSp" or "Del" or "←" or "→" or "Hme" or "End"
            || (k.Label.Length == 1 && char.IsAsciiDigit(k.Label[0])));
    }

    public bool TryType(KeyDef k, Action requestSecureAttention = null)
    {
        if (isSaving) return true;
        // Print Screen is a system action, not text input; let Press send it onward.
        if (k.Label == "PrtScr") return false;
        // Stage Ctrl/Alt locally so a dialog gesture cannot activate shortcuts in the previous document.
        if (k.Label is "Ctl" or "Alt")
        {
            if (!chordModifiers.Add(k)) chordModifiers.Remove(k);
            KeyAvailabilityChanged?.Invoke();
            return true;
        }
        bool secureAttention = SecureAttention.IsChord(k, chordModifiers);
        if (chordModifiers.Count > 0)
        {
            chordModifiers.Clear();
            KeyAvailabilityChanged?.Invoke();
        }
        if (secureAttention) { requestSecureAttention?.Invoke(); return true; }
        if (k.Label == "Enter") { _ = ApplyAndCloseAsync(); return true; }
        if (k.Label == "Esc") { Close(); return true; }
        if (activeBox == null) return true;
        var l = k.Label;
        if (l.Length == 1 && char.IsDigit(l[0])) Edit(l, 0, 0);          // siffra: skrivs in vid markören
        else if (l == "BkSp") Edit("", 1, 0);                             // tar bort tecknet före markören
        else if (l == "Del") Edit("", 0, 1);                              // tar bort tecknet efter markören
        else if (l == "←") activeBox.CaretIndex = Math.Max(0, activeBox.CaretIndex - 1);
        else if (l == "→") activeBox.CaretIndex = Math.Min(activeBox.Text.Length, activeBox.CaretIndex + 1);
        else if (l == "Hme") activeBox.CaretIndex = 0;
        else if (l == "End") activeBox.CaretIndex = activeBox.Text.Length;
        return true;   // övriga tangenter ignoreras medan en ruta är vald
    }

    // Tar bort "delBefore" tecken före markören och "delAfter" tecken efter den (eller den markerade texten,
    // om något är markerat) och skriver sedan in "insert" på platsen. Högst fyra tecken får plats.
    void Edit(string insert, int delBefore, int delAfter)
    {
        var t = activeBox.Text;
        int start = activeBox.SelectionStart, len = activeBox.SelectionLength;
        if (len == 0)
        {
            int from = Math.Max(0, start - delBefore);
            int to = Math.Min(t.Length, start + delAfter);
            start = from; len = to - from;
        }
        t = t.Remove(start, len);
        if (t.Length + insert.Length > activeBox.MaxLength) return;
        activeBox.Text = t.Insert(start, insert);
        activeBox.CaretIndex = start + insert.Length;
    }

    static bool ReadValue(TextBox field, TextBlock error, int min, int max, string unit, out int value)
    {
        bool valid = TrySettingValue(field, min, max, out value);
        error.Text = valid ? "" : $"Ange ett heltal mellan {min} och {max} {unit}.";
        error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        return valid;
    }

    static bool TrySettingValue(TextBox field, int min, int max, out int value)
        => int.TryParse(field.Text, out value) && value >= min && value <= max;

    // Visar de sparade värdena i rutan.
    public void Reload()
    {
        autoClick.IsChecked = cfg.AutoClick;
        dwell.Text = cfg.DwellMs.ToString();
        jitter.Text = cfg.JitterPx.ToString();
        repeat.IsChecked = cfg.Repeat;
        RefreshApplyButton();
    }

    public void RefreshAutoClickState()
    {
        autoClick.IsChecked = cfg.AutoClick;
        RefreshApplyButton();
    }

    bool HasChanges() => signInTimingPending || (autoClick.IsChecked == true) != cfg.AutoClick
        || (repeat.IsChecked == true) != cfg.Repeat || !int.TryParse(dwell.Text, out int delay) || delay != cfg.DwellMs
        || !int.TryParse(jitter.Text, out int tolerance) || tolerance != cfg.JitterPx;

    void RefreshApplyButton() => SoftKeyButtonDesign.SetApplyState(apply, HasChanges(), isSaving);

    async Task ApplyAndCloseAsync()
    {
        if (await ApplyAsync()) Close();
    }

    async Task<bool> ApplyAsync()
    {
        if (isSaving || !HasChanges()) return false;
        saveError.Visibility = Visibility.Collapsed;
        if (!Apply()) return false;
        if (shareTiming == null) return true;

        SetSaving(true);
        try
        {
            await shareTiming(AutoclickTimingSettings.From(cfg));
            signInTimingPending = false;
            return IsLoaded;
        }
        catch (InvalidOperationException ex)
        {
            signInTimingPending = true;
            saveError.Text = ex.Message;
            saveError.Visibility = Visibility.Visible;
            return false;
        }
        finally
        {
            SetSaving(false);
        }
    }

    void SetSaving(bool saving)
    {
        isSaving = saving;
        cancel.IsEnabled = autoClick.IsEnabled = repeat.IsEnabled = dwell.IsEnabled = jitter.IsEnabled = !saving;
        RefreshApplyButton();
        KeyAvailabilityChanged?.Invoke();
    }

    bool Apply()
    {
        bool validDwell = ReadValue(dwell, dwellError, Settings.MinDwellMs, Settings.MaxDwellMs, "ms", out int delay);
        bool validJitter = ReadValue(jitter, jitterError, Settings.MinJitterPx, Settings.MaxJitterPx, "px", out int tolerance);
        if (!validDwell || !validJitter)
        {
            Select(validDwell ? jitter : dwell);
            activeBox.SelectAll();
            return false;
        }
        var previous = (cfg.AutoClick, cfg.DwellMs, cfg.JitterPx, cfg.Repeat);
        cfg.AutoClick = autoClick.IsChecked == true;
        cfg.DwellMs = delay;
        cfg.JitterPx = tolerance;
        cfg.Repeat = repeat.IsChecked == true;
        if (save())
        {
            RefreshApplyButton();
            return true;
        }
        (cfg.AutoClick, cfg.DwellMs, cfg.JitterPx, cfg.Repeat) = previous;
        RefreshApplyButton();
        saveError.Text = "Kunde inte spara inställningarna. Försök igen.";
        saveError.Visibility = Visibility.Visible;
        return false;
    }
}
