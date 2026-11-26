using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

static class SignInDragKeySettings
{
    internal const string RegistryPath = @"SOFTWARE\SoftKey";
    internal const string RegistryValue = "SmallDragKeyIcons";

    public static bool ReadSmallSize()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(RegistryPath);
            return key?.GetValue(RegistryValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return false; }
    }

    public static void WriteSmallSize(IStartupStore store, bool small) =>
        store.Write(StartupHive.Machine, RegistryPath, RegistryValue, new(small ? 1 : 0, RegistryValueKind.DWord));
}
