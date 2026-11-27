using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

sealed class KeyboardSettingsProgressWindow : Window
{
    readonly Window keyboard;
    UIElement coveredContent;
    double originalOpacity;
    bool closed;

    public KeyboardSettingsProgressWindow(Window settings, bool applyToSignIn)
    {
        keyboard = settings.Owner ?? settings;
        Title = "Sparar tangentbordsinställningar…";
        Owner = keyboard;
        Topmost = keyboard.Topmost;
        ShowActivated = ShowInTaskbar = Focusable = false;
        SoftKeyPopupDesign.ConfigureWindow(this);
        AllowsTransparency = false;
        Background = SoftKeyPopupDesign.DialogSurface;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = keyboard.Left;
        Top = keyboard.Top;
        Width = keyboard.ActualWidth;
        Height = keyboard.ActualHeight;
        SourceInitialized += (_, _) => Native.NoActivate(new WindowInteropHelper(this).Handle);

        var message = new TextBlock
        {
            Text = Title, FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
        };
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        var layout = new StackPanel
        {
            MaxWidth = 344, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center,
            Children = { message }
        };
        if (applyToSignIn)
            layout.Children.Add(new TextBlock
            {
                Text = "Bekräfta i Windows om du får frågan.", FontSize = 12,
                Foreground = SoftKeyPopupDesign.ActionText, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0)
            });

        var progress = new ProgressBar
        {
            IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 16, 0, 0),
            Background = SoftKeyPopupDesign.ActionSurface, Foreground = SoftKeyPopupDesign.Accent,
            Template = SoftKeyPopupDesign.ProgressTemplate
        };
        AutomationProperties.SetName(progress, Title);
        layout.Children.Add(progress);
        Content = new Grid { Children = { layout } };

        Loaded += (_, _) =>
        {
            keyboard.SizeChanged += KeyboardResized;
            keyboard.LocationChanged += KeyboardMoved;
            PositionCover();
            settings.Closed += SettingsClosed;
            // Keep the keyboard in layout while preventing any intermediate frame from being visible.
            coveredContent = keyboard.Content as UIElement;
            if (coveredContent == null) return;
            originalOpacity = coveredContent.Opacity;
            coveredContent.SetCurrentValue(OpacityProperty, 0.0);
        };
        Closed += (_, _) =>
        {
            closed = true;
            keyboard.SizeChanged -= KeyboardResized;
            keyboard.LocationChanged -= KeyboardMoved;
            settings.Closed -= SettingsClosed;
            RestoreKeyboardContent();
        };
    }

    internal async Task FinishAsync()
    {
        if (closed) return;
        keyboard.UpdateLayout();
        PositionCover();
        RestoreKeyboardContent();
        // Let the updated keyboard render behind the opaque cover before dismissing it.
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (!closed) Close();
    }

    void RestoreKeyboardContent()
    {
        if (coveredContent == null) return;
        coveredContent.SetCurrentValue(OpacityProperty, originalOpacity);
        coveredContent = null;
    }

    void KeyboardResized(object sender, SizeChangedEventArgs e) => PositionCover();
    void KeyboardMoved(object sender, EventArgs e) => PositionCover();

    void SettingsClosed(object sender, EventArgs e) { if (!closed) Close(); }

    void PositionCover() => Native.CoverWithoutActivation(new WindowInteropHelper(this).Handle,
        new WindowInteropHelper(keyboard).Handle);
}
