using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

// Reusable visual language for compact SoftKey popup/settings windows.
static class SoftKeyPopupDesign
{
    public static readonly Brush WindowSurface = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF1));
    public static readonly Brush TitleBarSurface = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF3));
    public static readonly Brush GroupSurface = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
    public static readonly Brush Border = new SolidColorBrush(Color.FromRgb(0xD5, 0xD5, 0xD5));
    public static readonly Brush PrimaryText = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
    public static readonly Brush ActionSurface = new SolidColorBrush(Color.FromRgb(0xD9, 0xEA, 0xF7));
    public static readonly Brush ActionHover = new SolidColorBrush(Color.FromRgb(0xC8, 0xDE, 0xF0));
    public static readonly Brush ActionPressed = new SolidColorBrush(Color.FromRgb(0xB7, 0xD2, 0xE9));
    public static readonly Brush ActionBorder = new SolidColorBrush(Color.FromRgb(0x70, 0xA8, 0xD2));
    public static readonly Brush ActionText = new SolidColorBrush(Color.FromRgb(0x1F, 0x3B, 0x53));
    public static readonly Brush FieldSelected = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF2));
    public static readonly Brush DialogSurface = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xF9));
    public static readonly Brush DialogFooter = new SolidColorBrush(Color.FromRgb(0xEB, 0xF0, 0xF5));
    public static readonly Brush CardSurface = new SolidColorBrush(Color.FromRgb(0xFA, 0xFC, 0xFE));
    public static readonly Brush CardBorder = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xDE));
    public static readonly Brush CardHoverSurface = new SolidColorBrush(Color.FromRgb(0xF1, 0xF8, 0xFD));
    public static readonly Brush CardHoverBorder = new SolidColorBrush(Color.FromRgb(0x8D, 0xB8, 0xD1));
    public static readonly Brush MenuSurface = new SolidColorBrush(Color.FromRgb(0xF9, 0xF9, 0xF9));
    public static readonly Brush MenuHover = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA));
    public static readonly Brush MenuPressed = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5));
    public static readonly Thickness MenuRowPadding = new(8, 4, 8, 4);
    public static readonly Thickness MenuRowMargin = new(0, 1, 0, 1);
    public const double MenuRowHeight = 32;
    public const double TitleFontSize = 12;
    public const double CardCornerRadius = 10;

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
        var closeButton = SoftKeyButtonDesign.Close(close);

        var titleBar = new DockPanel
        {
            Height = 40, Background = TitleBarSurface, Margin = new Thickness(7, 1, 7, 0)
        };
        DockPanel.SetDock(closeButton, Dock.Right);
        titleBar.Children.Add(closeButton);
        titleBar.Children.Add(new TextBlock
        {
            Text = title, FontSize = TitleFontSize, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0)
        });
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button) return;
            if (e.LeftButton == MouseButtonState.Pressed) beginDrag();
        };

        var bodySurface = new Border
        {
            Background = DialogSurface, CornerRadius = new CornerRadius(0, 0, 8, 8), Child = body
        };
        return SurfaceFrame(new StackPanel { Children = { titleBar, bodySurface } }, TitleBarSurface);
    }

    public static Border SurfaceFrame(UIElement content, Brush surface) => new()
    {
        Margin = new Thickness(8), CornerRadius = new CornerRadius(9),
        Background = surface, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
        Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.2, BlurRadius = 12, ShadowDepth = 2, Direction = 270 },
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
            CornerRadius = new CornerRadius(0, 0, 8, 8),
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
                Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)), Margin = new Thickness(0, 0, 0, 8)
            });
        if (!string.IsNullOrWhiteSpace(description))
            content.Children.Add(new TextBlock
            {
                Text = description, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                Margin = new Thickness(0, -2, 0, 7)
            });
        foreach (var control in controls) content.Children.Add(control);
        return new Border
        {
            Background = GroupSurface, BorderBrush = Border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(11, 9, 11, 8),
            Margin = new Thickness(0, 0, 0, 9), Child = content
        };
    }

}
