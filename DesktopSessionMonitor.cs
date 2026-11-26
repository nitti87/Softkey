using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

sealed class DesktopSessionMonitor : IDisposable
{
    public const int SessionLock = 7; // WTS_SESSION_LOCK

    readonly Window window;
    readonly Action<int> notification;
    readonly Action poll;
    readonly DispatcherTimer timer;
    readonly int sessionId;
    HwndSource source;
    IntPtr handle;
    bool registered;
    long nextRegistration;
    public DesktopSessionMonitor(Window window, Action<int> notification, Action poll)
    {
        this.window = window; this.notification = notification; this.poll = poll;
        using var process = Process.GetCurrentProcess(); sessionId = process.SessionId;
        timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => { Register(); Poll(); };
        window.SourceInitialized += Started;
        window.Loaded += Loaded;
    }
    void Started(object sender, EventArgs e)
    {
        handle = new WindowInteropHelper(window).Handle;
        source = HwndSource.FromHwnd(handle);
        source.AddHook(Message);
        Register(); Poll(); timer.Start();
    }
    void Loaded(object sender, RoutedEventArgs e) => Poll();
    void Register()
    {
        if (registered || handle == IntPtr.Zero || Environment.TickCount64 < nextRegistration) return;
        nextRegistration = Environment.TickCount64 + 5000;
        registered = WTSRegisterSessionNotification(handle, 0); // NOTIFY_FOR_THIS_SESSION
    }
    void Poll()
    {
        try { poll(); }
        catch (Exception ex) { Trace.WriteLine("SoftKey desktop session check: " + ex.GetType().Name); }
    }
    IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x02B1 && lParam.ToInt64() == sessionId) // WM_WTSSESSION_CHANGE
        {
            try { notification((int)wParam.ToInt64()); }
            catch (Exception ex) { Trace.WriteLine("SoftKey desktop session event: " + ex.GetType().Name); }
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        timer.Stop();
        window.SourceInitialized -= Started; window.Loaded -= Loaded;
        if (registered) WTSUnRegisterSessionNotification(handle);
        source?.RemoveHook(Message);
    }
    [DllImport("wtsapi32.dll", SetLastError = true)] static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll")] static extern bool WTSUnRegisterSessionNotification(IntPtr window);
}
