using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

static class LogonRegistration
{
    static string SecureKeyboardVisitName(int sessionId) => "UnlockedSecureKeyboardTick." + sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool? ReadState()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(StartupConfiguration.AtPath);
            return key != null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    // The registered SYSTEM keyboard reports only a monotonic timestamp for its own session.
    public static void RecordSecureKeyboardVisit(int sessionId)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(StartupConfiguration.AtPath, writable: true);
            key?.SetValue(SecureKeyboardVisitName(sessionId), Environment.TickCount64, RegistryValueKind.QWord);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
    }

    public static long? ReadSecureKeyboardVisit(int sessionId)
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(StartupConfiguration.AtPath);
            return key?.GetValue(SecureKeyboardVisitName(sessionId)) as long?;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }
}
