using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

static class LockSession
{
    // Only the numeric WTSINFOEX prefix is copied. Account names and input data are never read.
    public static bool? IsLocked
    {
        get
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, -1, 25, out var buffer, out int bytes)) return null;
            try
            {
                if (bytes < 20) return null;
                var prefix = new byte[20];
                Marshal.Copy(buffer, prefix, 0, prefix.Length);
                return Decode(prefix);
            }
            finally { WTSFreeMemory(buffer); }
        }
    }

    internal static bool? Decode(ReadOnlySpan<byte> prefix)
    {
        // WTSINFOEX.Level is followed by its 8-byte-aligned union. SessionFlags is its third DWORD.
        if (prefix.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(prefix) != 1) return null;
        int state = BinaryPrimitives.ReadInt32LittleEndian(prefix.Slice(16, 4));
        return state switch { 0 => true, 1 => false, _ => null };
    }

    internal static bool CanStartLockedKeyboard(bool system, string desktop, bool? locked)
        => !system && string.Equals(desktop, "Default", StringComparison.OrdinalIgnoreCase) && locked == true;

    internal static bool ShouldStopLockedKeyboard(bool? locked, bool? registered)
        => locked == false || registered == false;

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    static extern bool WTSQuerySessionInformation(IntPtr server, int session, int info, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr buffer);
}
