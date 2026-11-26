using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

static class SoftKeyHoverTransition
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.RegisterAttached(
        "Background", typeof(Brush), typeof(SoftKeyHoverTransition), new PropertyMetadata(null, (target, change) =>
            Apply((FrameworkElement)target, Border.BackgroundProperty, (Brush)change.NewValue)));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.RegisterAttached(
        "BorderBrush", typeof(Brush), typeof(SoftKeyHoverTransition), new PropertyMetadata(null, (target, change) =>
            Apply((FrameworkElement)target, Border.BorderBrushProperty, (Brush)change.NewValue)));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.RegisterAttached(
        "Foreground", typeof(Brush), typeof(SoftKeyHoverTransition), new PropertyMetadata(null, (target, change) =>
            Apply((FrameworkElement)target, TextElement.ForegroundProperty, (Brush)change.NewValue)));

    public static TimeSpan Duration => TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 150 : 0);

    public static CubicEase Easing() => new() { EasingMode = EasingMode.EaseOut };

    public static void Apply(FrameworkElement target, DependencyProperty property, Brush next, bool animate = true)
    {
        var duration = Duration;
        if (!animate || !target.IsLoaded || duration == TimeSpan.Zero
            || target.TemplatedParent is ButtonBase { IsPressed: true }
            || target.GetValue(property) is not SolidColorBrush current
            || next is not SolidColorBrush destination || current.Color == destination.Color)
        {
            target.SetCurrentValue(property, next);
            return;
        }

        Color from = current.Color;
        Color to = destination.Color;
        // Preserve the visible hue when fading into or out of a transparent surface.
        if (from.A == 0) from = Color.FromArgb(0, to.R, to.G, to.B);
        if (to.A == 0) to = Color.FromArgb(0, from.R, from.G, from.B);
        var brush = new SolidColorBrush(destination.Color);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, to, duration)
        {
            EasingFunction = Easing(), FillBehavior = FillBehavior.Stop
        });
        target.SetCurrentValue(property, brush);
    }
}
