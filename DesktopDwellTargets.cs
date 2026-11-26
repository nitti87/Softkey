using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

// Local hit testing works on both desktops. External UI Automation is desktop-only.
sealed class DesktopDwellTargets
{
    internal readonly record struct Sample(Native.POINT Point, IntPtr Window, string RuntimeId, Rect HitBounds)
    {
        public object Identity => (Window, RuntimeId);
        public bool Contains(Native.POINT cursor) => (Point.X == cursor.X && Point.Y == cursor.Y)
            || (!HitBounds.IsEmpty && cursor.X >= HitBounds.Left && cursor.X < HitBounds.Right
                && cursor.Y >= HitBounds.Top && cursor.Y < HitBounds.Bottom);
    }

    readonly Dispatcher dispatcher;
    bool pending, useWindowOnly;
    IntPtr window;
    DwellTarget lastAction;
    int visit;
    Native.POINT lastPoint;
    double lastRequest = double.NegativeInfinity, started;

    public DesktopDwellTargets(Dispatcher dispatcher) => this.dispatcher = dispatcher;

    public static object FindLocal(Native.POINT point, FrameworkElement wordMenuAnchor)
    {
        var handle = Native.WindowFromPoint(point);
        if (HwndSource.FromHwnd(handle)?.RootVisual is not UIElement root) return handle;
        var hit = root.InputHitTest(root.PointFromScreen(new Point(point.X, point.Y))) as DependencyObject;
        for (var node = hit; node != null; node = Parent(node))
        {
            if (node == wordMenuAnchor) return node;
            if (node is ButtonBase or TextBoxBase or PasswordBox or ListBoxItem or ComboBox or ComboBoxItem or MenuItem or TabItem or Thumb)
                return (node, ((FrameworkElement)node).DataContext);
        }
        // Distinguish the native caption buttons as well as the empty part of each WPF window.
        return (handle, hit == null ? Native.HitTestAt(point, handle) : 1);
    }

    static DependencyObject Parent(DependencyObject node)
    {
        if (node is Visual or Visual3D) return VisualTreeHelper.GetParent(node);
        return (node is ContentElement content ? ContentOperations.GetParent(content) : null)
            ?? LogicalTreeHelper.GetParent(node);
    }

    public void Leave()
    {
        if (window == IntPtr.Zero) return;
        window = IntPtr.Zero;
        visit++;
    }

    public void Observe(Native.POINT point, DwellTarget action, double now, int version,
        Action<Sample, DwellTarget, int> receive)
    {
        var handle = Native.WindowFromPoint(point);
        if (handle == IntPtr.Zero) { Leave(); return; }
        if (handle != window || lastAction != action)
        {
            window = handle;
            lastAction = action;
            lastRequest = double.NegativeInfinity;
            useWindowOnly = false;
            visit++;
        }

        // A slow or hung provider must not hold up the keyboard, queue more workers,
        // or prevent dwell clicks in ordinary windows. Keep the fallback for this visit.
        if (pending && now - started >= 350) useWindowOnly = true;
        if (useWindowOnly)
        {
            receive(new Sample(point, handle, null, Rect.Empty), action, version);
            return;
        }
        if (pending || (lastPoint.X == point.X && lastPoint.Y == point.Y && now - lastRequest < 80)) return;
        pending = true;
        started = lastRequest = now;
        lastPoint = point;
        Query(point, handle, action, version, visit, receive);
    }

    async void Query(Native.POINT point, IntPtr handle, DwellTarget action, int version, int requestVisit,
        Action<Sample, DwellTarget, int> receive)
    {
        // UI Automation providers can call back into an application's UI thread.
        // Run the whole native query on an MTA worker and return only value data.
        var sample = await Task.Run(() => FindExternal(point, handle)).ConfigureAwait(false);
        if (dispatcher.HasShutdownStarted) return;
        _ = dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            pending = false;
            if (sample.HasValue && requestVisit == visit && !useWindowOnly)
                receive(sample.Value, action, version);
        }));
    }

    internal static Sample? FindExternal(Native.POINT point, IntPtr handle)
    {
        if (!Native.IsWindowAt(point, handle)) return null;
        try
        {
            var request = new CacheRequest();
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);
            request.Add(AutomationElement.RuntimeIdProperty);
            using (request.Activate())
            {
                var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
                if (element != null)
                {
                    var hit = element;
                    // Providers may expose a button's text and icon as separate leaves.
                    // Group those leaves under the actual interactive control.
                    for (int depth = 0; depth < 12 && element != null; depth++)
                    {
                        var type = element.Cached.ControlType;
                        if (type == ControlType.Window || type == ControlType.Document) break;
                        if (IsAction(type)) { hit = element; break; }
                        if (depth == 0 && type != ControlType.Text && type != ControlType.Image
                            && type != ControlType.Custom && type != ControlType.Pane) break;
                        element = TreeWalker.ControlViewWalker.GetParent(element, request);
                    }
                    var id = hit.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty) as int[];
                    // Allow small movements during the asynchronous query inside an atomic target.
                    // Containers may have nested targets, so only accept their exact sampled point.
                    var hitType = hit.Cached.ControlType;
                    var bounds = hitType == ControlType.Button || hitType == ControlType.CheckBox
                        || hitType == ControlType.RadioButton || hitType == ControlType.Hyperlink
                        ? hit.Cached.BoundingRectangle : Rect.Empty;
                    if (id?.Length > 0 && Native.IsWindowAt(point, handle))
                        return new Sample(point, handle, string.Join(",", id), bounds);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ElementNotAvailableException
            or ArgumentException or NotSupportedException or UnauthorizedAccessException or TimeoutException
            or System.ComponentModel.Win32Exception)
        {
            // Owner-drawn controls and disappearing or inaccessible providers can still use native dwell.
        }
        return Native.IsWindowAt(point, handle) ? new Sample(point, handle, null, Rect.Empty) : null;
    }

    static bool IsAction(ControlType type) => type == ControlType.Button || type == ControlType.CheckBox
        || type == ControlType.RadioButton || type == ControlType.Hyperlink || type == ControlType.Edit
        || type == ControlType.ListItem || type == ControlType.MenuItem || type == ControlType.TabItem
        || type == ControlType.TreeItem || type == ControlType.ComboBox || type == ControlType.DataItem
        || type == ControlType.Slider || type == ControlType.ScrollBar || type == ControlType.Thumb
        || type == ControlType.Spinner || type == ControlType.SplitButton;
}
