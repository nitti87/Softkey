using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

static class LogonDiagnostics
{
    static readonly Queue<object> events = new();
    // This bounded status history contains no window titles, account names, keys or typed text.
    public static void Record(string state)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            events.Enqueue(new { State = state, TimeUtc = DateTime.UtcNow.ToString("o"), ProcessId = Environment.ProcessId,
                Desktop = DesktopContext.CurrentName, SessionLocked = LockSession.IsLocked, InputDesktopActive = DesktopContext.IsCurrentInputDesktop });
            while (events.Count > 8) events.Dequeue();
            using var hive = RegistryKey.OpenBaseKey(identity.IsSystem ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
            string path = identity.IsSystem
                ? @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\SoftKey_Logon_v1"
                : @"SOFTWARE\SoftKey\LogonDiagnostics";
            using var key = identity.IsSystem ? hive.OpenSubKey(path, true) : hive.CreateSubKey(path);
            key?.SetValue("RecentStates", JsonSerializer.Serialize(events), RegistryValueKind.String);
            using var process = Process.GetCurrentProcess();
            key?.SetValue("RecentStates." + process.ProcessName, JsonSerializer.Serialize(events), RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
    }
}
