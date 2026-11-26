using System;
using System.Runtime.InteropServices;
using System.Text;

// Read desktop identities; never switch desktops or alter their security descriptors.
static class DesktopContext
{
    public static string CurrentName => Name(GetThreadDesktop(GetCurrentThreadId()));
    public static bool IsSecure => string.Equals(CurrentName, "Winlogon", StringComparison.OrdinalIgnoreCase);
    public static bool IsCurrentInputDesktop
    {
        get
        {
            IntPtr input = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
            if (input == IntPtr.Zero) return false;
            try
            {
                string current = CurrentName, active = Name(input);
                return current.Length > 0 && active.Length > 0 && string.Equals(current, active, StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseDesktop(input); }
        }
    }

    static string Name(IntPtr desktop)
    {
        if (desktop == IntPtr.Zero) return "";
        var value = new StringBuilder(256);
        return GetUserObjectInformation(desktop, 2, value, value.Capacity * 2, out _) ? value.ToString() : "";
    }

    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)]
    static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder value, int length, out int needed);
}
