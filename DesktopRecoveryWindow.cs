using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

// Runs in the signed startup helper and remains usable with a stalled keyboard dispatcher.
sealed class DesktopRecoveryWindow : Window
{
    const string RestartOutline = "M 3.5,10 A 6.5,6.5 0 1 1 10,16.5 M 3.5,5.5 V 10 H 8";
    readonly DwellTracker dwell = new();
    bool armed;

    public DesktopRecoveryWindow(Func<bool> stillUnresponsive, Func<bool> restart, int dwellMs, int jitterPx)
    {
        Title = "Starta om SoftKey?";
        SoftKeyPopupDesign.ConfigureWindow(this);
        ShowInTaskbar = false;
        Topmost = true;

        var heading = new TextBlock
        {
            Text = "SoftKey svarar inte", FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = SoftKeyPopupDesign.PrimaryText, TextWrapping = TextWrapping.Wrap
        };
        var description = SoftKeyPopupDesign.PromptCard(SoftKeyPopupDesign.ActionSurface, SoftKeyPopupDesign.Accent,
            RestartOutline, heading, "Vila pekaren på en knapp för att välja. Vid omstart kan osparade ändringar i SoftKey försvinna.");
        var error = new TextBlock
        {
            Text = "SoftKey kunde inte startas om. Försök igen.", Foreground = SoftKeyPopupDesign.ErrorText,
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0)
        };
        var content = new StackPanel();
        content.Children.Add(description);
        content.Children.Add(error);

        var cancel = SoftKeyButtonDesign.Action("Avbryt");
        cancel.IsCancel = cancel.IsDefault = true;
        var confirm = SoftKeyButtonDesign.Action("Starta om", true);
        confirm.MinHeight = cancel.MinHeight = 42;
        cancel.Click += (_, _) => Close();
        confirm.Click += (_, _) => Confirm();
        Content = SoftKeyPopupDesign.Frame(Title,
            SoftKeyPopupDesign.Sections(430, content, SoftKeyButtonDesign.Row(cancel, confirm)), Close, () => DragMove());

        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            if (!stillUnresponsive()) { Close(); return; }
            if (!Native.GetCursorPos(out var point)) { dwell.Reset(); return; }
            Button over = null;
            if (Native.IsWindowAt(point, new WindowInteropHelper(this).Handle))
            {
                if (Contains(confirm, point)) over = confirm;
                else if (Contains(cancel, point)) over = cancel;
            }
            // Require a fresh entry onto a button after the prompt appears.
            if (over == null) { armed = true; dwell.ObserveMovement(point.X, point.Y, jitterPx); dwell.Reset(); return; }
            if (!armed) return;
            double now = Environment.TickCount64;
            if (!dwell.Update(point.X, point.Y, DwellTarget.SoftKeyControl, now, Math.Max(1500, dwellMs), jitterPx, false, over)) return;
            dwell.Complete(now, false);
            if (over == confirm) Confirm();
            else Close();
        };
        Loaded += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();

        void Confirm()
        {
            if (!stillUnresponsive()) { Close(); return; }
            if (restart()) DialogResult = true;
            else error.Visibility = Visibility.Visible;
        }
    }

    static bool Contains(Button button, Native.POINT point)
        => new Rect(button.RenderSize).Contains(button.PointFromScreen(new Point(point.X, point.Y)));
}
