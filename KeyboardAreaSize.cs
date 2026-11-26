using System;
using System.Buffers.Binary;
using System.IO;
using System.Security;
using System.Text.Json.Serialization;
using Microsoft.Win32;

readonly record struct KeyboardAreaSize(int Width, int Height)
{
    internal const string RegistryPath = @"SOFTWARE\SoftKey";
    internal const string RegistryValue = "KeyboardAreaSize";

    [JsonIgnore]
    public bool IsValid => Width is >= 160 and <= 4096 && Height is >= 90 and <= 2160;

    public static KeyboardAreaSize? Measure(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width > 4096 || height > 2160 || width <= 0 || height <= 0)
            return null;
        var measured = new KeyboardAreaSize((int)Math.Round(width), (int)Math.Round(height));
        return measured.IsValid ? measured : null;
    }

    public static KeyboardAreaSize? ReadInstalled()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(RegistryPath);
            return Decode(key?.GetValue(RegistryValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return null; }
    }

    internal static KeyboardAreaSize? Decode(object value)
    {
        if (value is not byte[] { Length: 4 } bytes) return null;
        var size = new KeyboardAreaSize(BinaryPrimitives.ReadUInt16LittleEndian(bytes), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        return size.IsValid ? size : null;
    }

    internal StartupValue Encode()
    {
        if (!IsValid) throw new InvalidOperationException("Tangentbordets mått ligger utanför det tillåtna intervallet.");
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)Width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)Height);
        return new(bytes, RegistryValueKind.Binary);
    }
}
