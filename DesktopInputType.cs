using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

enum DesktopInputKind { Text, Email, Phone, Password }

static class DesktopInputType
{
    // Chromium and Firefox expose HTML input metadata through IAccessible2.
    // MSAA identifies the focused child. Only its object attributes are read.
    [ComImport, Guid("618736e0-3c3d-11cf-810c-00aa00389b71"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    interface IAccessibleFocus
    {
        [DispId(-5011)] object accFocus { get; }
        [DispId(-5002)] object get_accChild(object childId);
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAccessibleServices
    {
        [PreserveSig] int QueryService(in Guid service, in Guid iid, out IntPtr result);
    }

    static readonly Guid AccessibleId = typeof(IAccessibleFocus).GUID;
    static readonly Guid Accessible2Id = new("e89f726e-c4f4-4c19-bb19-b647d7fa8478");

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessibleFocus accessible);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ReadAttributes(IntPtr self, out IntPtr attributes);

    public static DesktopInputKind? FromBrowserFocus(IntPtr window, IntPtr active)
    {
        return AttributesAt(window) ?? (window != active ? AttributesAt(active) : null);
    }

    static DesktopInputKind? AttributesAt(IntPtr window)
    {
        const uint Client = unchecked((uint)-4);
        try
        {
            if (window == IntPtr.Zero || AccessibleObjectFromWindow(window, Client, AccessibleId, out var item) < 0)
                return null;
            for (int depth = 0; item != null && depth < 5; depth++)
            {
                var focused = item.accFocus;
                if (focused is int child)
                {
                    if (child == 0) return ReadType(item);
                    focused = item.get_accChild(child);
                }
                if (ReferenceEquals(focused, item)) return ReadType(item);
                if (focused is not IAccessibleFocus next) return null;
                // Some providers return an already-focused leaf that has no accFocus implementation.
                var type = ReadType(next);
                if (type.HasValue) return type;
                item = next;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        return null;
    }

    static DesktopInputKind? ReadType(IAccessibleFocus item)
    {
        if (item is not IAccessibleServices service || service.QueryService(AccessibleId, Accessible2Id, out var pointer) < 0
            || pointer == IntPtr.Zero) return null;
        try
        {
            // IUnknown + IDispatch + IAccessible have 28 slots. get_attributes is
            // the 18th method of IAccessible2 (Accessible2.idl).
            const int GetAttributesSlot = 45;
            var method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), GetAttributesSlot * IntPtr.Size);
            var read = Marshal.GetDelegateForFunctionPointer<ReadAttributes>(method);
            int result = read(pointer, out var attributes);
            try { return result >= 0 && attributes != IntPtr.Zero ? FromAttributes(Marshal.PtrToStringBSTR(attributes)) : null; }
            finally { if (attributes != IntPtr.Zero) Marshal.FreeBSTR(attributes); }
        }
        finally { Marshal.Release(pointer); }
    }

    internal static DesktopInputKind? FromAttributes(string attributes)
    {
        const string property = "text-input-type:";
        if (string.IsNullOrEmpty(attributes)) return null;
        for (int start = 0, i = 0; i <= attributes.Length; i++)
        {
            if (i < attributes.Length && attributes[i] == '\\') { i++; continue; }
            if (i < attributes.Length && attributes[i] != ';') continue;
            var entry = attributes.AsSpan(start, i - start);
            if (entry.StartsWith(property, StringComparison.Ordinal))
            {
                var type = entry[property.Length..];
                if (type.Equals("email", StringComparison.OrdinalIgnoreCase)) return DesktopInputKind.Email;
                if (type.Equals("tel", StringComparison.OrdinalIgnoreCase)) return DesktopInputKind.Phone;
                if (type.Equals("password", StringComparison.OrdinalIgnoreCase)) return DesktopInputKind.Password;
                return type.Equals("text", StringComparison.OrdinalIgnoreCase) ? null : DesktopInputKind.Text;
            }
            start = i + 1;
        }
        return null;
    }

    internal static DesktopInputKind FromLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return DesktopInputKind.Text;
        const string email = @"(?<!\p{L})(?:e[\s-]?mail(?:\s*address)?|e[\s-]?post(?:\s*adress)?|mejladress|mailadress)(?!\p{L})";
        const string phone = @"(?<!\p{L})(?:telefon(?:nummer)?|mobilnummer|phone(?:\s*number)?|tel)(?!\p{L})";
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        // Automation IDs commonly use camel case or underscores where a visible label would use spaces.
        label = Regex.Replace(label, "(?<=[a-z])(?=[A-Z])", " ").Replace('_', ' ');
        // Sign-in fields often accept an email address or a phone number in the same box.
        if (Regex.IsMatch(label, email, options)) return DesktopInputKind.Email;
        return Regex.IsMatch(label, phone, options) ? DesktopInputKind.Phone : DesktopInputKind.Text;
    }
}
