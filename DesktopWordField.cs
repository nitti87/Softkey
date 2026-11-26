using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

// Preserve the identity of a focused field, including when several fields share one HWND.
sealed record DesktopWordField(IntPtr Active, IntPtr Focus, string ElementId, DesktopInputKind Kind = DesktopInputKind.Text)
{
    public bool IsPassword => Kind == DesktopInputKind.Password;
    public bool CanInsertText { get; init; } = true;
}

sealed class DesktopWordFieldReader
{
    internal const int WordCheckTimeoutMs = 40;
    const int MaxActiveProviders = 4;
    readonly Dictionary<uint, Task<(DesktopWordField Field, string EmailPrefix)>> pending = new();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder name, int maxLength);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out UIntPtr result);

    // A fresh check is essential even when the HWND is unchanged (browsers, WPF).
    // A slow or unavailable provider only pauses word features. The key is still sent.
    public DesktopWordField Read() => ReadField(false);

    public DesktopWordField ReadInsertionField() => ReadField(true);

    DesktopWordField ReadField(bool includePassword)
    {
        var target = Native.InputTarget();
        var check = BeginRead(target, includePassword);
        return check?.Wait(includePassword ? 150 : WordCheckTimeoutMs) == true && Native.InputTarget() == target ? check.Result.Field : null;
    }

    public Task<(DesktopWordField Field, string EmailPrefix)> BeginRead((IntPtr Active, IntPtr Focus) target,
        bool includePassword = false, bool includeEmailPrefix = false)
    {
        if (target.Focus == IntPtr.Zero || target.Active == IntPtr.Zero) return null;
        GetWindowThreadProcessId(target.Focus, out uint process);
        if (process == 0 || process == (uint)Environment.ProcessId) return null;

        return BeginProviderRead(process, () =>
        {
            var field = ReadFocusedField(target.Active, target.Focus, process, includePassword, out var element);
            return (field, includeEmailPrefix ? ReadEmailPrefix(field, element) : null);
        });
    }

    internal Task<(DesktopWordField Field, string EmailPrefix)> BeginProviderRead(uint process,
        Func<(DesktopWordField Field, string EmailPrefix)> read)
    {
        // A provider can outlive the caller's timeout. Keep it from blocking the
        // next app while bounding the number of outstanding native calls.
        foreach (var entry in pending)
            if (entry.Value.IsCompleted) pending.Remove(entry.Key);
        if (pending.ContainsKey(process) || pending.Count >= MaxActiveProviders) return null;
        var task = Task.Run(read);
        pending.Add(process, task);
        return task;
    }

    static DesktopWordField ReadFocusedField(IntPtr active, IntPtr focus, uint process, bool includePassword,
        out AutomationElement focusedElement)
    {
        focusedElement = null;
        string windowClass = WindowClass(focus);
        bool nativeEdit = string.Equals(windowClass, "Edit", StringComparison.OrdinalIgnoreCase);
        bool nativeWritable = true;
        if (nativeEdit)
        {
            const int PasswordStyle = 0x20, ReadOnlyStyle = 0x800;
            const uint GetPasswordChar = 0x00D2, AbortIfHung = 2;
            int style = GetWindowLong(focus, -16);
            nativeWritable = (style & ReadOnlyStyle) == 0;
            if ((style & PasswordStyle) != 0)
                return includePassword ? new(active, focus, null, DesktopInputKind.Password) { CanInsertText = nativeWritable } : null;
            if (SendMessageTimeout(focus, GetPasswordChar, IntPtr.Zero, IntPtr.Zero,
                AbortIfHung, 25, out var mask) == IntPtr.Zero) return null;
            if (mask != UIntPtr.Zero)
                return includePassword ? new(active, focus, null, DesktopInputKind.Password) { CanInsertText = nativeWritable } : null;
        }

        bool browser = IsBrowserClass(windowClass) || (focus != active && IsBrowserClass(WindowClass(active)));
        AutomationElement element = null;
        var field = ReadElement(() => element = AutomationElement.FocusedElement, active, focus, process, nativeEdit || includePassword,
            browser ? () => DesktopInputType.FromBrowserFocus(focus, active) : null);
        focusedElement = element;
        if (field?.IsPassword == true && !includePassword) return null;
        if (field != null) return nativeWritable ? field : field with { CanInsertText = false };
        return nativeEdit ? new DesktopWordField(active, focus, null) { CanInsertText = nativeWritable } : null;
    }

    static string WindowClass(IntPtr handle)
    {
        var buffer = new StringBuilder(80);
        GetClassName(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    static bool IsBrowserClass(string name) => name.StartsWith("Chrome_", StringComparison.Ordinal)
        || name.StartsWith("Mozilla", StringComparison.Ordinal);

    internal static DesktopWordField ReadElement(Func<AutomationElement> getElement, IntPtr active, IntPtr focus, uint process,
        bool includePassword = false, Func<DesktopInputKind?> readInputType = null)
    {
        try
        {
            var request = new CacheRequest();
            request.Add(AutomationElement.IsPasswordProperty);
            request.Add(AutomationElement.ProcessIdProperty);
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.IsEnabledProperty);
            request.Add(AutomationElement.IsTextPatternAvailableProperty);
            request.Add(ValuePattern.IsReadOnlyProperty);
            using (request.Activate())
            {
                var element = getElement();
                if (element == null || element.Cached.ProcessId != process
                    || element.GetCachedPropertyValue(AutomationElement.IsPasswordProperty, true) is not bool password
                    || (password && !includePassword))
                    return null;
                var role = element.Cached.ControlType;
                var readOnly = element.GetCachedPropertyValue(ValuePattern.IsReadOnlyProperty, true);
                bool hasText = element.GetCachedPropertyValue(AutomationElement.IsTextPatternAvailableProperty, true) is true;
                // Chromium uses ComboBox for both text autocomplete and choice-only dropdowns.
                bool textEdit = role == ControlType.Edit || (role == ControlType.ComboBox && readOnly is false && hasText);
                // A browser's contenteditable host can be an accessible Group without ValuePattern.
                bool editableGroup = role == ControlType.Group && hasText && readOnly is not true && IsWritableText(element);
                if (!textEdit && !editableGroup && role != ControlType.Document) return null;
                var id = element.GetRuntimeId();
                if (id == null || id.Length == 0) return null;
                DesktopInputKind kind = DesktopInputKind.Text;
                if (password) kind = DesktopInputKind.Password;
                else if (textEdit)
                {
                    kind = readInputType?.Invoke() ?? ClassifyEditLabel(element);
                    if (kind == DesktopInputKind.Password && !includePassword) return null;
                }
                bool writable = element.GetCachedPropertyValue(AutomationElement.IsEnabledProperty, true) is not false
                    && (readOnly is bool locked ? !locked : textEdit || editableGroup || IsWritableText(element));
                return new(active, focus, string.Join(".", id), kind) { CanInsertText = writable };
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return null;
        }
    }

    static bool IsWritableText(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) && pattern is TextPattern text
                && text.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false;
        }
        catch (Exception ex) when (IsProviderFailure(ex)) { return false; }
    }

    static DesktopInputKind ClassifyEditLabel(AutomationElement element)
    {
        var kind = DesktopInputType.FromLabel(element.Current.Name);
        return kind != DesktopInputKind.Text ? kind : DesktopInputType.FromLabel(element.Current.AutomationId);
    }

    static bool IsProviderFailure(Exception ex) => ex is ElementNotAvailableException or ElementNotEnabledException
        or InvalidOperationException or ArgumentException or COMException or UnauthorizedAccessException
        or System.ComponentModel.Win32Exception or TimeoutException or NotSupportedException;

    internal static string ReadEmailPrefix(DesktopWordField field, AutomationElement element)
    {
        if (field?.Kind != DesktopInputKind.Email || element == null) return "";
        try
        {
            if (element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true) is false
                && element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern value
                && !value.Current.IsReadOnly)
                return value.Current.Value ?? "";
        }
        catch (Exception ex) when (IsProviderFailure(ex)) { }
        return "";
    }

    internal static bool MatchesEmailPrefix(string address, string prefix)
        => prefix.Length > 0 && address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    public static Task<bool> WriteEmail(DesktopWordField expected, string address, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var start = Stopwatch.GetTimestamp();
        var target = (expected.Active, expected.Focus);
        if (cancellationToken.IsCancellationRequested || expected.Kind != DesktopInputKind.Email || Native.InputTarget() != target) return false;
        GetWindowThreadProcessId(expected.Focus, out uint process);
        if (process == 0 || process == (uint)Environment.ProcessId) return false;
        try
        {
            AutomationElement element = null;
            bool browser = IsBrowserClass(WindowClass(expected.Focus)) || IsBrowserClass(WindowClass(expected.Active));
            var field = ReadElement(() => element = AutomationElement.FocusedElement, expected.Active, expected.Focus, process,
                readInputType: browser ? () => DesktopInputType.FromBrowserFocus(expected.Focus, expected.Active) : null);
            if (field != expected || !MatchesEmailPrefix(address, ReadEmailPrefix(field, element))
                || Stopwatch.GetElapsedTime(start).TotalMilliseconds > 500
                || Native.InputTarget() != target || !Native.CanSendInput() || Native.ModifierDown) return false;
            return SetEmailValue(element, address, () => !cancellationToken.IsCancellationRequested && Stopwatch.GetElapsedTime(start).TotalMilliseconds <= 500
                && Native.InputTarget() == target && Native.CanSendInput() && !Native.ModifierDown);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return false;
        }
    });

    internal static bool SetEmailValue(AutomationElement element, string address, Func<bool> stillAllowed)
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)
            || pattern is not ValuePattern value || value.Current.IsReadOnly || !stillAllowed()) return false;
        value.SetValue(address);
        return true;
    }
}
