using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

// Reusable visual language for compact SoftKey popup/settings windows.
static class SoftKeyPopupDesign
{
    public static readonly Brush DialogSurface = SoftKeyKeyDesign.WindowSurface;
    public static readonly Brush TitleBarSurface = SoftKeyKeyDesign.FunctionFace;
    public static readonly Brush DialogFooter = SoftKeyKeyDesign.FunctionFace;
    public static readonly Brush CardSurface = SoftKeyKeyDesign.LetterFace;
    public static readonly Brush CardBorder = SoftKeyKeyDesign.MutedDivider;
    public static readonly Brush Border = SoftKeyKeyDesign.Outline;
    public static readonly Brush ControlOutline = new SolidColorBrush(Color.FromRgb(0x82, 0x7D, 0x73));
    public static readonly Brush PrimaryText = SoftKeyKeyDesign.PrimaryText;
    public static readonly Brush SecondaryText = SoftKeyKeyDesign.SecondaryText;
    public static readonly Brush OnAccentText = Brushes.White;
    public static readonly Brush Accent = SoftKeyKeyDesign.SlateAccent;
    public static readonly Brush ActionText = SoftKeyKeyDesign.SlateAccentText;
    public static readonly Brush ActionSurface = SoftKeyKeyDesign.AccentFace;
    public static readonly Brush ActionHover = SoftKeyKeyDesign.KeyFlash;
    public static readonly Brush ActionPressed = new SolidColorBrush(Color.FromRgb(0xBA, 0xCD, 0xDC));
    public static readonly Brush ActionBorder = SoftKeyKeyDesign.SlateOutline;
    public static readonly Brush FieldSelected = ActionSurface;
    public static readonly Brush CardHoverSurface = SoftKeyKeyDesign.KeyHover;
    public static readonly Brush CardHoverBorder = ActionBorder;
    public static readonly Brush MenuSurface = CardSurface;
    public static readonly Brush MenuHover = SoftKeyKeyDesign.HeaderHover;
    public static readonly Brush MenuPressed = SoftKeyKeyDesign.ModifierFace;
    public static readonly Brush ErrorText = new SolidColorBrush(Color.FromRgb(0x9F, 0x30, 0x24));
    public static readonly Brush ErrorSurface = new SolidColorBrush(Color.FromRgb(0xFA, 0xEC, 0xE6));
    public static readonly Brush ErrorBorder = new SolidColorBrush(Color.FromRgb(0xD8, 0xA9, 0x9B));
    public static readonly Thickness MenuRowPadding = new(8, 4, 8, 4);
    public static readonly Thickness MenuRowMargin = new(0, 1, 0, 1);
    public const double MenuRowHeight = 32;
    public const double TitleFontSize = 12;
    public const double CardCornerRadius = 10;
    const double FrameCornerRadius = 11;

    static readonly Style TextBoxStyle = InputStyle(typeof(TextBox));
    static readonly Style PasswordBoxStyle = InputStyle(typeof(PasswordBox));

    public static readonly ControlTemplate ProgressTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ProgressBar}">
            <Border x:Name="PART_Track" Background="{TemplateBinding Background}" CornerRadius="3" ClipToBounds="True">
                <Grid x:Name="PART_Indicator" HorizontalAlignment="Left" ClipToBounds="True">
                    <Border x:Name="Fill" Background="{TemplateBinding Foreground}" CornerRadius="3"/>
                    <Border x:Name="PART_GlowRect" Width="84" HorizontalAlignment="Left" CornerRadius="3"
                            Background="{TemplateBinding Foreground}" Visibility="Collapsed"/>
                </Grid>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property="IsIndeterminate" Value="True">
                    <Setter TargetName="Fill" Property="Visibility" Value="Collapsed"/>
                    <Setter TargetName="PART_GlowRect" Property="Visibility" Value="Visible"/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    static Style InputStyle(Type type)
    {
        var style = new Style(type);
        style.Setters.Add(new Setter(Control.BackgroundProperty, CardSurface));
        style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryText));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, ControlOutline));
        style.Setters.Add(new Setter(TextBox.SelectionBrushProperty, Accent));
        style.Setters.Add(new Setter(TextBox.SelectionTextBrushProperty, OnAccentText));
        style.Setters.Add(new Setter(TextBox.CaretBrushProperty, PrimaryText));
        return style;
    }

    public static void ConfigureWindow(Window window, Action<Size> fitToWorkArea = null)
    {
        window.Foreground = PrimaryText;
        window.FontFamily = new FontFamily("Segoe UI");
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.UseLayoutRounding = true;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = SizeToContent.WidthAndHeight;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Resources[typeof(TextBox)] = TextBoxStyle;
        window.Resources[typeof(PasswordBox)] = PasswordBoxStyle;
        window.Loaded += CenterOnLoad;

        void CenterOnLoad(object sender, RoutedEventArgs args)
        {
            window.Loaded -= CenterOnLoad;
            if (window.WindowStartupLocation != WindowStartupLocation.CenterScreen) return;
            CenterOnOwnerMonitor(window, fitToWorkArea);
        }
    }

    internal static void CenterOnOwnerMonitor(Window window, Action<Size> fitToWorkArea = null)
    {
        // Follow the main SoftKey window even when another dialog owns this one.
        var anchor = window;
        while (anchor.Owner != null) anchor = anchor.Owner;
        var anchorHandle = new WindowInteropHelper(anchor).Handle;
        if (!Native.GetWindowRect(anchorHandle, out var anchorBounds)
            || !WindowPositioning.TryWorkArea(anchorBounds, out var area)) return;

        if (fitToWorkArea != null)
        {
            var dpi = VisualTreeHelper.GetDpi(anchor);
            fitToWorkArea(new Size((area.Right - area.Left) / dpi.DpiScaleX, (area.Bottom - area.Top) / dpi.DpiScaleY));
            window.UpdateLayout();
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (!Native.GetWindowRect(handle, out var bounds)) return;
        int x = area.Left + Math.Max(0, (area.Right - area.Left - (bounds.Right - bounds.Left)) / 2);
        int y = area.Top + Math.Max(0, (area.Bottom - area.Top - (bounds.Bottom - bounds.Top)) / 2);
        Native.MoveWithoutActivation(handle, x, y);
    }

    public static void KeepResizeInWorkArea(Window window)
    {
        window.SizeChanged += (_, _) =>
        {
            if (window.IsLoaded) window.Dispatcher.InvokeAsync(KeepInsideWorkArea, DispatcherPriority.Loaded);
        };

        void KeepInsideWorkArea()
        {
            if (!window.IsVisible) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (!Native.GetWindowRect(handle, out var bounds)) return;
            // Use the monitor with the title bar even when expanding across its edge.
            var titleEdge = bounds;
            titleEdge.Bottom = bounds.Top + 1;
            if (!WindowPositioning.TryWorkArea(titleEdge, out var area)) return;
            int left = Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - (bounds.Right - bounds.Left)));
            int top = Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - (bounds.Bottom - bounds.Top)));
            if (left != bounds.Left || top != bounds.Top) Native.MoveWithoutActivation(handle, left, top);
        }
    }

    public static Border Frame(string title, UIElement body, Action close, Action beginDrag)
    {
        var titleBar = new DockPanel
        {
            Height = 40, Background = TitleBarSurface, Margin = new Thickness(7, 1, 7, 0)
        };
        if (close != null)
        {
            var closeButton = SoftKeyButtonDesign.Close(close);
            DockPanel.SetDock(closeButton, Dock.Right);
            titleBar.Children.Add(closeButton);
        }
        titleBar.Children.Add(new TextBlock
        {
            Text = title, FontSize = TitleFontSize, FontWeight = FontWeights.SemiBold, Foreground = PrimaryText,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0)
        });
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button) return;
            if (e.LeftButton == MouseButtonState.Pressed) beginDrag?.Invoke();
        };

        var bodySurface = new Border
        {
            Background = DialogSurface, CornerRadius = new CornerRadius(0, 0, CardCornerRadius, CardCornerRadius),
            Child = body
        };
        return SurfaceFrame(new StackPanel { Children = { titleBar, bodySurface } }, TitleBarSurface);
    }

    public static Border SurfaceFrame(UIElement content, Brush surface) => new()
    {
        Margin = new Thickness(8), CornerRadius = new CornerRadius(FrameCornerRadius),
        Background = surface, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
        Effect = new DropShadowEffect
        {
            Color = ((SolidColorBrush)PrimaryText).Color, Opacity = 0.22, BlurRadius = 12, ShadowDepth = 2, Direction = 270
        },
        Child = content
    };

    public static StackPanel Sections(double width, UIElement content, UIElement footer, Thickness? contentPadding = null)
    {
        var panel = new StackPanel { Width = width };
        panel.Children.Add(new Border
        {
            Padding = contentPadding ?? new Thickness(16, 14, 16, 14), Child = content
        });
        panel.Children.Add(new Border
        {
            Background = DialogFooter, BorderBrush = CardBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, CardCornerRadius, CardCornerRadius),
            Padding = new Thickness(16, 10, 16, 12), Child = footer
        });
        return panel;
    }

    public static Border Separator(Thickness margin) => new()
    {
        Height = 1, Background = Border, Opacity = 0.6, Margin = margin, IsHitTestVisible = false
    };

    public static Border Group(string title, string description, params UIElement[] controls)
    {
        var content = new StackPanel();
        if (!string.IsNullOrWhiteSpace(title))
            content.Children.Add(new TextBlock
            {
                Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryText, Margin = new Thickness(0, 0, 0, 8)
            });
        if (!string.IsNullOrWhiteSpace(description))
            content.Children.Add(new TextBlock
            {
                Text = description, FontSize = 11,
                Foreground = SecondaryText,
                Margin = new Thickness(0, -2, 0, 7)
            });
        foreach (var control in controls) content.Children.Add(control);
        return new Border
        {
            Background = CardSurface, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardCornerRadius), Padding = new Thickness(11, 9, 11, 8),
            Margin = new Thickness(0, 0, 0, 9), Child = content
        };
    }

}
