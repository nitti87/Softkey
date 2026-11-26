using System;
using System.ComponentModel;
using System.Diagnostics;

static class StartupPriority
{
    // High is the highest non-realtime class. Realtime can starve Windows mouse/keyboard input.
    public static void Apply()
    {
        try { using var process = Process.GetCurrentProcess(); process.PriorityClass = ProcessPriorityClass.High; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        { Trace.WriteLine("SoftKey priority unavailable: " + ex.GetType().Name); }
    }
}
