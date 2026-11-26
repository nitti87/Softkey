using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

enum SecureAttentionResult { Ready, Requested, InactiveDesktop, WrongSession, MissingUIAccess, UacDisabled, PolicyBlocked, Unavailable }
sealed record SecureAttentionContext(bool UIAccess, bool System, bool SecureDesktop, bool ActiveDesktop, bool? Locked, bool UacEnabled, int? Policy);

static class SecureAttention
{
    const string PolicyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    internal static bool IsChord(KeyDef key, IEnumerable<KeyDef> held)
        => key.Label == "Del" && key.Ext && key.Scan == 0x53
            && held.Any(k => k.Mod && k.Scan == 0x1D)
            && held.Any(k => k.Mod && k.Scan == 0x38 && !k.Ext);

    internal static SecureAttentionResult CheckSupport(SecureAttentionContext context)
    {
        if (!context.UIAccess) return SecureAttentionResult.MissingUIAccess;
        if (!context.UacEnabled) return SecureAttentionResult.UacDisabled;
        if (context.Policy is 2 or 3 || (context.Policy == null && context.SecureDesktop)) return SecureAttentionResult.Ready;
        return SecureAttentionResult.PolicyBlocked;
    }
    internal static SecureAttentionResult Request(SecureAttentionContext context, bool restricted, Action<bool> sender)
    {
        if (!context.ActiveDesktop) return SecureAttentionResult.InactiveDesktop;
        if (context.SecureDesktop ? !restricted || !context.System : context.System || context.Locked != restricted)
            return SecureAttentionResult.WrongSession;
        var support = CheckSupport(context);
        if (support != SecureAttentionResult.Ready) return support;
        sender(!context.System);
        // SendSAS is void: this records a request, never a claim that Windows changed screens.
        return SecureAttentionResult.Requested;
    }

    internal static SecureAttentionContext Context()
    {
        using var identity = WindowsIdentity.GetCurrent();
        bool uiAccess = GetTokenInformation(identity.AccessToken, 26, out int value, sizeof(int), out _) && value != 0;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(PolicyPath);
        int? Number(string name)
        {
            object number = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return number == null ? null : key.GetValueKind(name) == RegistryValueKind.DWord && number is int n ? n : int.MinValue;
        }
        return new(uiAccess, identity.IsSystem, DesktopContext.IsSecure, DesktopContext.IsCurrentInputDesktop,
            LockSession.IsLocked, Number("EnableLUA") == 1, Number("SoftwareSASGeneration"));
    }
    public static SecureAttentionResult Request(bool restricted)
    {
        try { return Request(Context(), restricted, SendSAS); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or UnauthorizedAccessException or SecurityException or IOException) { return SecureAttentionResult.Unavailable; }
    }
    public static bool Probe()
    {
        try
        {
            if (CheckSupport(Context()) != SecureAttentionResult.Ready
                || !NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, "sas.dll"), out var module)) return false;
            try { return NativeLibrary.TryGetExport(module, "SendSAS", out _); }
            finally { NativeLibrary.Free(module); }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException) { return false; }
    }
    [DllImport("sas.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern void SendSAS([MarshalAs(UnmanagedType.Bool)] bool asUser);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(SafeAccessTokenHandle token, int information, out int value, int length, out int needed);
}
