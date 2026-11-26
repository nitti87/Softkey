using System;
using System.Buffers.Binary;
using System.IO;
using System.Security;
using Microsoft.Win32;

readonly record struct AutoclickTimingSettings(int DwellMs, int JitterPx)
{
    const string RegistryPath = @"SOFTWARE\SoftKey";
    const string RegistryValue = "AutoclickTiming";

    bool IsValid => DwellMs is >= Settings.MinDwellMs and <= Settings.MaxDwellMs
        && JitterPx is >= Settings.MinJitterPx and <= Settings.MaxJitterPx;

    public static AutoclickTimingSettings From(Settings settings) => new(settings.DwellMs, settings.JitterPx);

    public static AutoclickTimingSettings? ReadInstalled()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(RegistryPath);
            return Decode(key?.GetValue(RegistryValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return null; }
    }

    internal static AutoclickTimingSettings? Decode(object value)
    {
        if (value is not byte[] { Length: 3 } bytes) return null;
        var timing = new AutoclickTimingSettings(BinaryPrimitives.ReadUInt16LittleEndian(bytes), bytes[2]);
        return timing.IsValid ? timing : null;
    }

    public void WriteInstalled(IStartupStore store)
    {
        if (!IsValid) throw new InvalidOperationException("Autoklickvärdena ligger utanför det tillåtna intervallet.");
        var bytes = new byte[3];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)DwellMs);
        bytes[2] = (byte)JitterPx;
        store.Write(StartupHive.Machine, RegistryPath, RegistryValue, new(bytes, RegistryValueKind.Binary));
    }

    public void ApplyTo(Settings settings)
    {
        settings.DwellMs = DwellMs;
        settings.JitterPx = JitterPx;
    }
}
