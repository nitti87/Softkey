using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

sealed class KeyboardSettingsWindow : Window
{
    const double PreviewKeyboardHeight = 216;
    const double KeyboardPageWidth = 552, OtherSettingsPageWidth = SoftKeySettingsDesign.Width;
    readonly record struct Choices(KeyboardLayoutSettings Layout, bool ApplyToSignIn, bool MenuAbove);
    readonly Slider horizontal = new(), vertical = new(), fontSize = new();
    readonly List<Button> sampleKeys = new();
    internal bool IsSaving { get; private set; }
    readonly TextBlock error = new()
    {
        Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 8, 2, 0), Visibility = Visibility.Collapsed
    };

    public KeyboardSettingsWindow(Settings settings, bool canApplyToSignIn,
        Func<KeyboardLayoutSettings, bool, bool, Task<bool>> save)
    {
        Title = "Tangentbordsinställningar";
        ShowActivated = ShowInTaskbar = false;
        Topmost = settings.AlwaysOnTop;

        var controls = new StackPanel();
        controls.Children.Add(BuildPreview());
        var spacing = new StackPanel { Children = { SoftKeySettingsDesign.Heading("Tangentavstånd") } };
        spacing.Children.Add(SliderRow("Horisontellt mellanrum", horizontal, settings.HorizontalKeyGap,
            Settings.MinKeyGap, Settings.MaxKeyGap, 1, "px", scaleWith: fontSize));
        spacing.Children.Add(SoftKeyPopupDesign.Separator(new Thickness(0, 8, 0, 8)));
        spacing.Children.Add(SliderRow("Vertikalt mellanrum", vertical, settings.VerticalKeyGap,
            Settings.MinKeyGap, Settings.MaxKeyGap, 1, "px", scaleWith: fontSize));
        var spacingCard = SoftKeySettingsDesign.Card(spacing);
        spacingCard.Margin = new Thickness(0, 0, 0, 10);
        controls.Children.Add(spacingCard);
        controls.Children.Add(SoftKeySettingsDesign.Card(SliderRow("Teckenstorlek", fontSize, settings.KeyFontSizePercent,
            Settings.MinKeyFontSizePercent, Settings.MaxKeyFontSizePercent, 5, "%")));
        SoftKeyCheckBox signIn = null;
        if (canApplyToSignIn)
        {
            const string label = "Använd samma inställningar vid Windows-inloggning";
            signIn = SoftKeySettingsDesign.CheckOption(label,
                "Spara tangenternas mått, mellanrum och teckenstorlek även för låsskärmen och PIN-skärmen.",
                SoftKeySettingsDesign.Icon(SoftKeySettingsDesign.LockGlyph));
            signIn.Margin = new Thickness(0, 10, 0, 0);
            signIn.IsChecked = settings.ShareKeyboardLayoutAtSignIn;
            controls.Children.Add(signIn);
        }

        var beside = SoftKeySettingsDesign.RadioOption("DesktopMenuPlacement", "Bredvid DragKey", "Menyn ligger kvar när titelraden döljs.",
            SoftKeySettingsDesign.Icon("M 2,6 H 7 M 11,6 H 18 M 2,12 H 18 M 2,16 H 18"));
        var above = SoftKeySettingsDesign.RadioOption("DesktopMenuPlacement", "Ovanför DragKey", "Menyn döljs tillsammans med titelraden.",
            SoftKeySettingsDesign.Icon("M 2,3 H 7 M 11,7 H 18 M 2,12 H 18 M 2,16 H 18"));
        beside.IsChecked = !settings.MenuAboveDragKey;
        above.IsChecked = settings.MenuAboveDragKey;
        beside.Margin = new Thickness(0, 0, 0, 10);
        var otherSettings = new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 6),
            Children =
            {
                SoftKeySettingsDesign.Heading("Menyplacering på skrivbordet"),
                beside,
                above
            }
        };
        var tabs = SoftKeyTabDesign.Headers("Tangentbord", "Övriga inställningar");
        var keyboardPage = new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var otherPage = new ScrollViewer
        {
            Content = otherSettings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed
        };
        var pages = new Grid { Margin = new Thickness(0, 12, 0, 0), Children = { keyboardPage, otherPage } };
        tabs.SelectedIndex = 0;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        UpdatePreview();

        var cancel = SoftKeyButtonDesign.Action("Avbryt");
        var apply = SoftKeyButtonDesign.Apply();
        var ok = SoftKeyButtonDesign.Action("OK", true);
        var applied = ReadChoices();
        bool signInPending = false;
        cancel.IsCancel = ok.IsDefault = true;
        cancel.Click += (_, _) => Close();
        bool closed = false;
        apply.Click += async (_, _) => await SaveAsync(false);
        ok.Click += async (_, _) => await SaveAsync(true);
        horizontal.ValueChanged += LayoutEdited;
        vertical.ValueChanged += LayoutEdited;
        fontSize.ValueChanged += LayoutEdited;
        above.Checked += ChoiceEdited;
        above.Unchecked += ChoiceEdited;
        if (signIn != null)
        {
            signIn.Checked += ChoiceEdited;
            signIn.Unchecked += ChoiceEdited;
        }
        RefreshApplyButton();

        Choices ReadChoices() => new(new KeyboardLayoutSettings((int)horizontal.Value, (int)vertical.Value, (int)fontSize.Value),
            signIn?.IsChecked == true, above.IsChecked == true);
        Choices ReadStoredChoices() => new(KeyboardLayoutSettings.From(settings),
            canApplyToSignIn && settings.ShareKeyboardLayoutAtSignIn, settings.MenuAboveDragKey);
        bool HasChanges(Choices choices) => choices != applied || (signInPending && choices.ApplyToSignIn);

        void LayoutEdited(object sender, RoutedPropertyChangedEventArgs<double> args)
        {
            UpdatePreview();
            RefreshApplyButton();
        }

        void ChoiceEdited(object sender, RoutedEventArgs args)
        {
            error.Visibility = Visibility.Collapsed;
            RefreshApplyButton();
        }

        void RefreshApplyButton() => SoftKeyButtonDesign.SetApplyState(apply, HasChanges(ReadChoices()), IsSaving);

        async Task SaveAsync(bool closeAfterSave)
        {
            if (IsSaving) return;
            var choices = ReadChoices();
            if (!HasChanges(choices))
            {
                if (closeAfterSave) Close();
                return;
            }
            var storedBefore = ReadStoredChoices();
            IsSaving = true;
            SetSaveControls(false);
            error.Visibility = Visibility.Collapsed;
            bool saved = false;
            KeyboardSettingsProgressWindow progress = null;
            try
            {
                progress = new KeyboardSettingsProgressWindow(this, choices.ApplyToSignIn);
                progress.Show();
                await Dispatcher.Yield(DispatcherPriority.Background);
                if (closed) return;
                saved = await save(choices.Layout, choices.ApplyToSignIn, choices.MenuAbove);
                error.Text = "Kunde inte spara inställningarna. Försök igen.";
            }
            catch (InvalidOperationException ex)
            {
                error.Text = ex.Message;
            }
            finally
            {
                if (!closed && progress != null) await progress.FinishAsync();
                if (saved)
                {
                    applied = choices;
                    signInPending = false;
                }
                else
                {
                    // A Windows sign-in save can fail after the desktop values were written.
                    var storedAfter = ReadStoredChoices();
                    if (storedAfter != storedBefore)
                    {
                        applied = storedAfter;
                        signInPending = storedAfter.ApplyToSignIn;
                    }
                }
                IsSaving = false;
                if (!closed) SetSaveControls(true);
            }
            if (closed) return;
            if (!saved) error.Visibility = Visibility.Visible;
            else if (closeAfterSave) Close();
        }

        void SetSaveControls(bool enabled)
        {
            tabs.IsEnabled = pages.IsEnabled = cancel.IsEnabled = ok.IsEnabled = enabled;
            RefreshApplyButton();
        }

        Closing += (_, e) => { if (IsSaving && Owner?.IsVisible == true) e.Cancel = true; };
        Closed += (_, _) => closed = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };

        var pageViewport = new StackPanel { ClipToBounds = true, Children = { pages } };
        var content = new StackPanel { Children = { tabs, pageViewport, error } };
        var panel = SoftKeyPopupDesign.Sections(KeyboardPageWidth, content, SoftKeyButtonDesign.Row(cancel, apply, ok));
        double availableWidth = KeyboardPageWidth;
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs) FitActiveTab(animate: true);
        };
        Content = SoftKeyPopupDesign.Frame(Title, panel, Close, () => { if (!IsSaving) DragMove(); });
        SoftKeyPopupDesign.ConfigureWindow(this, available =>
        {
            availableWidth = Math.Max(280, available.Width - 20);
            pages.MaxHeight = Math.Max(80, available.Height - 200);
            FitActiveTab();
        });
        SoftKeyPopupDesign.KeepResizeInWorkArea(this);

        void FitActiveTab(bool animate = false)
        {
            double oldWidth = panel.ActualWidth, contentInset = oldWidth - content.ActualWidth;
            bool showOther = tabs.SelectedIndex == 1;
            keyboardPage.Visibility = showOther ? Visibility.Collapsed : Visibility.Visible;
            otherPage.Visibility = showOther ? Visibility.Visible : Visibility.Collapsed;
            double newWidth = Math.Min(showOther ? OtherSettingsPageWidth : KeyboardPageWidth, availableWidth);
            panel.Width = newWidth;
            cancel.Width = apply.Width = ok.Width = Math.Min(90, (newWidth - 44) / 3);

            SoftKeyTabDesign.ResizePage(pageViewport, pages, newWidth - contentInset, animate);
            SoftKeyTabDesign.AnimateSize(panel, WidthProperty, oldWidth, newWidth, animate);
        }
    }

    FrameworkElement BuildPreview()
    {
        var sample = KeyboardWindow.BuildKeyDeck(PreviewKey, out _);
        sample.Width = KeyboardLayout.KeyAreaWeight;
        sample.Height = PreviewKeyboardHeight;
        sample.UseLayoutRounding = sample.SnapsToDevicePixels = true;
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Förhandsvisning", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = SoftKeyPopupDesign.ActionText, Margin = new Thickness(1, 0, 0, 9)
        });
        content.Children.Add(new Viewbox { StretchDirection = StretchDirection.DownOnly, Child = sample, IsEnabled = false });
        return new Border
        {
            Background = SoftKeyPopupDesign.TitleBarSurface, BorderBrush = SoftKeyPopupDesign.CardBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(12, 10, 12, 12),
            Child = content, IsHitTestVisible = false
        };

        Button PreviewKey(KeyDef key)
        {
            var button = KeyboardWindow.CreatePreviewKey(key);
            sampleKeys.Add(button);
            return button;
        }
    }

    void UpdatePreview()
    {
        double baseline = KeyboardLayout.LegendFontSize(PreviewKeyboardHeight / KeyboardLayout.Rows.Length,
            KeyboardLayout.KeyboardColumnWeight / KeyboardLayout.KeyUnitsPerRow);
        int percent = (int)fontSize.Value;
        foreach (var key in sampleKeys)
        {
            KeyboardWindow.SetKeySpacing(key, (int)horizontal.Value, (int)vertical.Value, percent);
            KeyboardWindow.SetKeyLegendSize(key, baseline, percent);
        }
        error.Visibility = Visibility.Collapsed;
    }

    static FrameworkElement SliderRow(string label, Slider slider, int value, int minimum, int maximum,
        int step, string unit, Slider scaleWith = null)
    {
        slider.Minimum = minimum;
        slider.Maximum = maximum;
        slider.Value = value;
        slider.SmallChange = step;
        slider.LargeChange = step;
        slider.TickFrequency = step;
        slider.IsSnapToTickEnabled = true;
        slider.IsMoveToPointEnabled = true;
        slider.Template = SliderTemplate;
        slider.Height = 36;
        slider.Background = SoftKeyPopupDesign.FieldSelected;
        slider.Foreground = SoftKeyPopupDesign.Accent;
        AutomationProperties.SetName(slider, label);

        var amount = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = SoftKeyPopupDesign.ActionText };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
        var badge = new Border
        {
            MinWidth = 52, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 3, 7, 3),
            Background = SoftKeyPopupDesign.ActionSurface, Child = amount
        };
        amount.TextAlignment = TextAlignment.Center;
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        header.Children.Add(new TextBlock
        {
            Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        var stepDown = StepButton("−", "Minska " + label.ToLowerInvariant(), -1);
        var stepUp = StepButton("+", "Öka " + label.ToLowerInvariant(), 1);
        void ShowValue()
        {
            int chosen = (int)slider.Value;
            int scaled = scaleWith is { Value: > 0 } ? KeyboardLayout.ScaledGap(chosen, (int)scaleWith.Value) : chosen;
            amount.Text = scaled == chosen ? $"{chosen} {unit}" : $"{chosen} → {scaled} {unit}";
            stepDown.IsEnabled = slider.Value > slider.Minimum;
            stepUp.IsEnabled = slider.Value < slider.Maximum;
            stepDown.Opacity = stepDown.IsEnabled ? 1 : 0.45;
            stepUp.Opacity = stepUp.IsEnabled ? 1 : 0.45;
        }
        slider.ValueChanged += (_, _) => ShowValue();
        if (scaleWith != null) scaleWith.ValueChanged += (_, _) => ShowValue();
        ShowValue();

        Button StepButton(string text, string help, int amount)
        {
            var button = SoftKeyButtonDesign.Action(text);
            button.Width = button.Height = 36;
            button.FontSize = 16;
            button.FontWeight = FontWeights.SemiBold;
            button.Margin = new Thickness(0);
            button.ToolTip = help;
            AutomationProperties.SetName(button, help);
            button.Click += (_, _) => slider.Value += amount * step;
            return button;
        }

        var rail = new DockPanel();
        DockPanel.SetDock(stepDown, Dock.Left);
        DockPanel.SetDock(stepUp, Dock.Right);
        rail.Children.Add(stepDown);
        rail.Children.Add(stepUp);
        slider.Margin = new Thickness(8, 0, 8, 0);
        rail.Children.Add(slider);
        return new StackPanel { Children = { header, rail } };
    }

    static readonly ControlTemplate SliderTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Slider}">
            <Grid Background="Transparent">
                <Border Background="{TemplateBinding Background}" CornerRadius="3" Height="6"
                        VerticalAlignment="Center" Margin="10,0"/>
                <Track x:Name="PART_Track">
                    <Track.DecreaseRepeatButton>
                        <RepeatButton Command="{x:Static Slider.DecreaseLarge}" Focusable="False">
                            <RepeatButton.Template>
                                <ControlTemplate TargetType="{x:Type RepeatButton}">
                                    <Grid Background="Transparent">
                                        <Border Height="6" VerticalAlignment="Center" CornerRadius="3"
                                                Background="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Slider}}"/>
                                    </Grid>
                                </ControlTemplate>
                            </RepeatButton.Template>
                        </RepeatButton>
                    </Track.DecreaseRepeatButton>
                    <Track.Thumb>
                        <Thumb Width="24">
                            <Thumb.Template>
                                <ControlTemplate TargetType="{x:Type Thumb}">
                                    <Grid Background="Transparent">
                                        <Ellipse x:Name="Handle" Width="19" Height="19" Fill="White" StrokeThickness="2"
                                                 Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Slider}}"/>
                                    </Grid>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="Handle" Property="Fill" Value="#D9EAF7"/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Thumb.Template>
                        </Thumb>
                    </Track.Thumb>
                    <Track.IncreaseRepeatButton>
                        <RepeatButton Command="{x:Static Slider.IncreaseLarge}" Focusable="False">
                            <RepeatButton.Template>
                                <ControlTemplate TargetType="{x:Type RepeatButton}">
                                    <Border Background="Transparent"/>
                                </ControlTemplate>
                            </RepeatButton.Template>
                        </RepeatButton>
                    </Track.IncreaseRepeatButton>
                </Track>
            </Grid>
        </ControlTemplate>
        """);
}
