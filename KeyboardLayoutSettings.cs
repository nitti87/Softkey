using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

readonly record struct KeyboardLayoutSettings(int HorizontalGap, int VerticalGap, int FontSizePercent = Settings.DefaultKeyFontSizePercent)
{
    const string RegistryPath = @"SOFTWARE\SoftKey";
    const string RegistryValue = "KeyboardLayout";
    const string LegacySpacingValue = "KeyboardSpacing";
    public static KeyboardLayoutSettings Default => new(Settings.DefaultKeyGap, Settings.DefaultKeyGap);

    public bool IsValid => HorizontalGap is >= Settings.MinKeyGap and <= Settings.MaxKeyGap
        && VerticalGap is >= Settings.MinKeyGap and <= Settings.MaxKeyGap
        && FontSizePercent is >= Settings.MinKeyFontSizePercent and <= Settings.MaxKeyFontSizePercent;

    public static KeyboardLayoutSettings From(Settings settings) =>
        new(settings.HorizontalKeyGap, settings.VerticalKeyGap, settings.KeyFontSizePercent);

    public static bool IsInstalled
    {
        get
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd(Path.DirectorySeparatorChar);
            return AppContext.BaseDirectory.StartsWith(programFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static KeyboardLayoutSettings? ReadInstalled()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(RegistryPath);
            return Decode(key?.GetValue(RegistryValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames))
                ?? Decode(key?.GetValue(LegacySpacingValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return null; }
    }

    internal static KeyboardLayoutSettings? Decode(object value)
    {
        if (value is not byte[] { Length: 2 or 3 } bytes) return null;
        if (bytes[0] > Settings.MaxKeyGap || bytes[1] > Settings.MaxKeyGap) return null;
        int fontSize = bytes.Length == 3 ? bytes[2] : Settings.DefaultKeyFontSizePercent;
        var layout = new KeyboardLayoutSettings(Math.Max(Settings.MinKeyGap, (int)bytes[0]),
            Math.Max(Settings.MinKeyGap, (int)bytes[1]), fontSize);
        return layout.IsValid ? layout : null;
    }

    public void WriteInstalled(IStartupStore store, KeyboardAreaSize? area = null)
    {
        if (!IsValid) throw new InvalidOperationException("Tangentbordsinställningarna ligger utanför det tillåtna intervallet.");
        // Store the three related settings together. Older copies can still read their spacing record.
        var layout = new StartupValue(new byte[] { (byte)HorizontalGap, (byte)VerticalGap, (byte)FontSizePercent }, RegistryValueKind.Binary);
        if (area == null) { store.Write(StartupHive.Machine, RegistryPath, RegistryValue, layout); return; }

        var previousArea = store.Read(StartupHive.Machine, KeyboardAreaSize.RegistryPath, KeyboardAreaSize.RegistryValue);
        store.Write(StartupHive.Machine, KeyboardAreaSize.RegistryPath, KeyboardAreaSize.RegistryValue, area.Value.Encode());
        try { store.Write(StartupHive.Machine, RegistryPath, RegistryValue, layout); }
        catch
        {
            if (previousArea == null) store.DeleteValue(StartupHive.Machine, KeyboardAreaSize.RegistryPath, KeyboardAreaSize.RegistryValue);
            else store.Write(StartupHive.Machine, KeyboardAreaSize.RegistryPath, KeyboardAreaSize.RegistryValue, previousArea);
            throw;
        }
    }

    public void ApplyTo(Settings settings)
    {
        settings.HorizontalKeyGap = HorizontalGap;
        settings.VerticalKeyGap = VerticalGap;
        settings.KeyFontSizePercent = FontSizePercent;
    }
}
