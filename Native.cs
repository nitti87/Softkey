using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Pratar med Windows: skickar tangenttryck och musklick, och styr fönsterstilar.
// Den här filen behöver du sällan ändra.
static class Native
{
    internal static bool RequireActiveDesktop { get; set; }
    internal static Func<bool> AdditionalInputGuard { get; set; }
    static readonly Dictionary<(IntPtr Layout, uint ScanCode), uint> VirtualKeyCache = new();
    static readonly Dictionary<(IntPtr Layout, ushort Scan, bool Extended, bool Shift, bool AltGr, bool Caps), string> CharacterCache = new();
    static readonly byte[] NeutralKeyboardState = new byte[256];
    const uint MapVirtualKeyToCharacter = 2, NoKeyboardStateChange = 4;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO
    {
        public int Size;
        public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretRect;
    }

    const uint KeyUp = 2;
    static readonly int InputSize = Marshal.SizeOf<INPUT>();
    static Func<INPUT[], uint> inputSender = inputs => SendInput((uint)inputs.Length, inputs, InputSize);
    static readonly int GuiThreadInfoSize = Marshal.SizeOf<GUITHREADINFO>();

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern short GetKeyState(int key);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern short VkKeyScanEx(char character, IntPtr keyboardLayout);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState, StringBuilder buffer, int bufferSize, uint flags, IntPtr keyboardLayout);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint command);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint length);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetSystemMenu(IntPtr window, bool revert);
    [DllImport("user32.dll")] static extern int EnableMenuItem(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] static extern bool DrawMenuBar(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool AdjustWindowRectExForDpi(ref RECT rect, int style, bool menu, int extendedStyle, uint dpi);
    [DllImport("user32.dll", EntryPoint = "IsIconic")] public static extern bool IsMinimized(IntPtr window);

    public static int HitTestAt(POINT point, IntPtr window)
    {
        if (!IsWindowAt(point, window)) return 0;
        int coordinates = unchecked((int)(((uint)(ushort)point.Y << 16) | (ushort)point.X));
        return (int)SendMessage(window, WmNcHitTest, IntPtr.Zero, new IntPtr(coordinates)).ToInt64();
    }

    public static bool IsCaptionAt(POINT point, IntPtr window) => HitTestAt(point, window) == 2;

    public static bool MoveWithoutActivation(IntPtr window, int x, int y)
        => SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); // no size, z-order or focus change

    public static bool ResizeWithoutActivation(IntPtr window, int x, int y, int width, int height)
        => SetWindowPos(window, IntPtr.Zero, x, y, width, height, 0x0004 | 0x0010);

    public static void CoverWithoutActivation(IntPtr cover, IntPtr target)
    {
        if (cover == IntPtr.Zero || !GetWindowRect(target, out var bounds)) return;
        const uint PreviousWindow = 3; // GW_HWNDPREV
        const uint NoSize = 0x0001, NoMove = 0x0002, NoZOrder = 0x0004, NoActivate = 0x0010, NoOwnerZOrder = 0x0200;
        IntPtr above = GetWindow(target, PreviousWindow);
        uint flags = NoActivate | NoOwnerZOrder;
        if (above == cover) flags |= NoZOrder;
        else if (above == IntPtr.Zero || IsTopmost(above) != IsTopmost(target))
            above = IsTopmost(target) ? new IntPtr(-1) : IntPtr.Zero; // first window in the target's z-order band
        if (GetWindowRect(cover, out var current) && current.Equals(bounds)) flags |= NoSize | NoMove;
        if ((flags & (NoSize | NoMove | NoZOrder)) == (NoSize | NoMove | NoZOrder)) return;
        SetWindowPos(cover, above, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, flags);
    }

    static uint Send(INPUT[] inputs)
    {
        if (!CanSendInput()) return 0;
        uint sent = inputSender(inputs);
        if (sent != inputs.Length)
            Trace.WriteLine($"SendInput sent {sent} of {inputs.Length} events. Win32 error: {Marshal.GetLastWin32Error()}");
        return sent;
    }

    internal static bool CanSendInput() => (!RequireActiveDesktop || DesktopContext.IsCurrentInputDesktop)
        && (AdditionalInputGuard?.Invoke() ?? true);

    // Vid en delvis lyckad sekvens kan den sista nedtryckningen behöva släppas.
    static bool SendPairs(INPUT[] inputs)
    {
        uint sent = Send(inputs);
        if (sent == inputs.Length) return true;
        if ((sent & 1) != 0) Send(new[] { inputs[sent] });
        return false;
    }

    public static bool CapsLockOn => (GetKeyState(0x14) & 1) != 0;

    public static IntPtr DesktopInputLayout()
    {
        var target = InputTarget();
        var window = target.Focus != IntPtr.Zero ? target.Focus : target.Active;
        uint thread = window != IntPtr.Zero ? GetWindowThreadProcessId(window, out _) : 0;
        return GetKeyboardLayout(thread);
    }

    public static bool LeftMouseButtonDown => GetAsyncKeyState(0x01) < 0;
    public static bool RightMouseButtonDown => GetAsyncKeyState(0x02) < 0;
    public static bool MiddleMouseButtonDown => GetAsyncKeyState(0x04) < 0;

    public static bool SetLeftMouseButton(bool down) => Send(new[] { MouseInput(down ? 2u : 4u) }) == 1;
    public static bool SetRightMouseButton(bool down) => Send(new[] { MouseInput(down ? 0x08u : 0x10u) }) == 1;
    public static bool SetMiddleMouseButton(bool down) => Send(new[] { MouseInput(down ? 0x20u : 0x40u) }) == 1;

    public static bool ModifierDown =>
        GetAsyncKeyState(0x10) < 0 || GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x12) < 0
        || GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;

    public static IntPtr CurrentKeyboardLayout
    {
        get
        {
            if (RequireActiveDesktop)
            {
                // Winlogon's selected input language may differ from the SYSTEM thread's default.
                IntPtr foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                {
                    uint thread = GetWindowThreadProcessId(foreground, out _);
                    if (thread != 0) return GetKeyboardLayout(thread);
                }
            }
            return GetKeyboardLayout(0);
        }
    }

    public static string TranslateKey(IntPtr layout, ushort scan, bool extended, bool shift, bool altGr, bool capsLock)
    {
        const uint MAPVK_VSC_TO_VK_EX = 3;
        var characterKey = (layout, scan, extended, shift, altGr, capsLock);
        if (CharacterCache.TryGetValue(characterKey, out var cached)) return cached;

        uint scanCode = (uint)scan | (extended ? 0xE000u : 0u);
        var virtualKeyKey = (layout, scanCode);
        if (!VirtualKeyCache.TryGetValue(virtualKeyKey, out uint virtualKey))
        {
            virtualKey = MapVirtualKeyEx(scanCode, MAPVK_VSC_TO_VK_EX, layout);
            VirtualKeyCache[virtualKeyKey] = virtualKey;
        }
        if (virtualKey == 0)
        {
            CharacterCache[characterKey] = null;
            return null;
        }

        var state = new byte[256];
        if (shift)
        {
            state[0x10] = 0x80; // VK_SHIFT
            state[0xA0] = 0x80; // VK_LSHIFT
        }
        if (altGr)
        {
            state[0x11] = state[0xA2] = 0x80; // VK_CONTROL / VK_LCONTROL
            state[0x12] = state[0xA5] = 0x80; // VK_MENU / VK_RMENU
        }
        if (capsLock) state[0x14] = 1; // VK_CAPITAL toggle bit

        // A read-only translation can still compose with an accent queued for the
        // next real keystroke. Never store that temporary result in the layout cache.
        string accentBefore = PendingKeyboardAccent(layout);
        string value = ReadKeyboardCharacter(virtualKey, scanCode, state, layout, out int count);
        string accent = accentBefore ?? PendingKeyboardAccent(layout);
        if (accent == null) CharacterCache[characterKey] = value;
        else if (count > 0 && value != null)
            value = RemovePendingAccent(value, accent, IsDirectKeyboardCharacter(value, virtualKey, layout, shift, altGr, capsLock));
        return value;
    }

    internal static string PendingKeyboardAccent(IntPtr layout)
    {
        const uint space = 0x20, spaceScan = 0x39;
        string value = ReadKeyboardCharacter(space, spaceScan, NeutralKeyboardState, layout, out int count);
        if (count <= 0 || value is null or " ") return null;
        char nominalSpace = (char)MapVirtualKeyEx(space, MapVirtualKeyToCharacter, layout);
        if (value[^1] == nominalSpace) return value.Length == 1 ? null : value[..^1];
        return value;
    }

    static string ReadKeyboardCharacter(uint virtualKey, uint scanCode, byte[] state, IntPtr layout, out int count)
    {
        var output = new StringBuilder(8);
        count = ToUnicodeEx(virtualKey, scanCode, state, output, output.Capacity, NoKeyboardStateChange, layout);
        return count == 0 || output.Length == 0 ? null : output.ToString(0, Math.Min(Math.Abs(count), output.Length));
    }

    static bool IsDirectKeyboardCharacter(string value, uint virtualKey, IntPtr layout, bool shift, bool altGr, bool capsLock)
    {
        if (value.Length > 1) value = value.Normalize(NormalizationForm.FormC);
        if (value.Length != 1) return false;
        short mapping = VkKeyScanEx(value[0], layout);
        if (mapping == -1 || (mapping & 0xFF) != virtualKey) return false;
        int modifiers = (altGr ? 6 : 0) | ((shift ^ (capsLock && char.IsLetter(value[0]))) ? 1 : 0);
        return (mapping >> 8) == modifiers;
    }

    internal static string RemovePendingAccent(string value, string accent, bool directlyMapped)
    {
        if (value.Length > accent.Length && value.StartsWith(accent, StringComparison.Ordinal))
            return value[accent.Length..];
        if (directlyMapped || System.Globalization.StringInfo.GetNextTextElementLength(value.AsSpan()) != value.Length) return value;

        string mark = accent switch
        {
            "`" => "\u0300",
            "'" => "\u0301",
            "^" => "\u0302",
            "~" => "\u0303",
            "\"" => "\u0308",
            _ => accent.Normalize(NormalizationForm.FormKD).Trim()
        };
        if (mark.Length != 1 || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(mark[0])
            is not (System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark))
            return value;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        int index = decomposed.LastIndexOf(mark, StringComparison.Ordinal);
        return index > 0 ? decomposed.Remove(index, 1).Normalize(NormalizationForm.FormC) : value;
    }

    // Gör att fönstret aldrig tar fokus när man klickar på det (WS_EX_NOACTIVATE)
    public static void NoActivate(IntPtr h) => SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000);

    public const int WmSysCommand = 0x0112;
    public const int WmMouseActivate = 0x0021;
    public const int WmNcCalcSize = 0x0083;
    public const int WmNcHitTest = 0x0084;
    public const int WmNcPaint = 0x0085;
    public const int WmNcActivate = 0x0086;
    public const int HtClient = 1, HtTop = 12;
    public const int MaNoActivate = 3;
    public const int ScMinimize = 0xF020, ScMaximize = 0xF030, ScClose = 0xF060;

    internal static IntPtr ActivateWithoutRepaintingFrame(IntPtr window, IntPtr active)
        // Update Windows' activation state without painting over the extended client area.
        => DefWindowProc(window, WmNcActivate, active, new IntPtr(-1));

    internal static bool TryGetResizeFrame(IntPtr window, out RECT frame, out uint dpi)
    {
        frame = default;
        dpi = GetDpiForWindow(window);
        return dpi != 0 && AdjustWindowRectExForDpi(ref frame, GetWindowLong(window, -16), false, GetWindowLong(window, -20), dpi);
    }

    internal static POINT ScreenPointFromMessage(IntPtr lParam)
    {
        long coordinates = lParam.ToInt64();
        return new POINT { X = unchecked((short)coordinates), Y = unchecked((short)(coordinates >> 16)) };
    }

    internal static int HitTestResizeBorder(IntPtr window, POINT point, RECT frame)
    {
        if (!GetWindowRect(window, out var bounds) || point.Y < bounds.Top || point.Y >= bounds.Bottom
            || point.X < bounds.Left || point.X >= bounds.Right) return 0;
        bool left = point.X < bounds.Left - frame.Left, right = point.X >= bounds.Right - frame.Right;
        const int Left = 10, Right = 11, TopLeft = 13, TopRight = 14, Bottom = 15, BottomLeft = 16, BottomRight = 17;
        if (point.Y < bounds.Top - frame.Top) return left ? TopLeft : right ? TopRight : HtTop;
        if (point.Y >= bounds.Bottom - frame.Bottom) return left ? BottomLeft : right ? BottomRight : Bottom;
        return left ? Left : right ? Right : 0;
    }

    internal static void RecalculateWindowFrame(IntPtr window)
    {
        const uint RefreshFrame = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, RefreshFrame);
    }

    public static void SetWindowButtonsEnabled(IntPtr window, bool enabled)
    {
        const int GwlStyle = -16, StateButtons = 0x00020000 | 0x00010000, Caption = 0x00C00000;
        const uint MF_ENABLED = 0x0000, MF_GRAYED = 0x0001;
        if (window == IntPtr.Zero) return;

        int style = GetWindowLong(window, GwlStyle);
        SetWindowLong(window, GwlStyle, enabled ? style | StateButtons : style & ~StateButtons);
        RecalculateWindowFrame(window);

        IntPtr menu = GetSystemMenu(window, false);
        if (menu != IntPtr.Zero && EnableMenuItem(menu, ScClose, enabled ? MF_ENABLED : MF_GRAYED) != -1
            && (style & Caption) == Caption)
            DrawMenuBar(window);
    }

    // Används av den virtuella tangentbordsytan utan att ta tangentbordsfokus från målapplikationen.
    public static IntPtr ForegroundWindow => GetForegroundWindow();

    public static (IntPtr Active, IntPtr Focus) InputTarget()
    {
        var info = new GUITHREADINFO { Size = GuiThreadInfoSize };
        return GetGUIThreadInfo(0, ref info) ? (info.Active, info.Focus) : default;
    }

    // Windows owns the PIN input. Its internal focus can change independently of the broker window.
    internal static IntPtr WindowsHelloInputWindow() => ForegroundSystemWindow("CredentialUIBroker.exe");

    internal static bool IsWindowsConsentForeground => ForegroundSystemWindow("consent.exe") != IntPtr.Zero;

    static IntPtr ForegroundSystemWindow(string executable)
    {
        var foreground = ForegroundWindow;
        if (foreground == IntPtr.Zero) return IntPtr.Zero;
        GetWindowThreadProcessId(foreground, out uint owner);
        if (owner == 0) return IntPtr.Zero;
        using var process = OpenProcess(0x1000, false, owner); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid) return IntPtr.Zero;
        var image = new StringBuilder(512);
        uint length = (uint)image.Capacity;
        return QueryFullProcessImageName(process, 0, image, ref length)
            && string.Equals(image.ToString(), System.IO.Path.Combine(Environment.SystemDirectory, executable),
                StringComparison.OrdinalIgnoreCase) && ForegroundWindow == foreground ? foreground : IntPtr.Zero;
    }

    public static bool IsSoftKeyWindowAt(POINT point)
        => OwnsWindow(WindowFromPoint(point));

    public static bool OwnsWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }

    public static bool IsWindowAt(POINT point, IntPtr window) => WindowFromPoint(point) == window;
    internal static bool WindowIsVisible(IntPtr window) => IsWindowVisible(window);
    internal static bool SetWindowVisibility(IntPtr window, bool visible)
        => SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | (visible ? 0x0040u : 0x0080u));

    public static bool RestoreForegroundWindow(IntPtr previous)
    {
        if (previous == IntPtr.Zero || !IsWindow(previous)) return false;
        var current = GetForegroundWindow();
        if (current == previous) return true;
        GetWindowThreadProcessId(current, out uint processId);
        // Återställ bara om SoftKey fortfarande är aktivt. Överskriv inte ett nytt fönster
        // som användaren själv hunnit aktivera medan popupen var öppen.
        if (processId != (uint)Environment.ProcessId) return false;
        return SetForegroundWindow(previous);
    }

    // Används när inställningen slås av eller på; hoppa över onödiga stiländringar.
    public static bool IsTopmost(IntPtr h)
        => h != IntPtr.Zero && (GetWindowLong(h, -20) & 0x00000008) != 0;

    public static bool IsWindowAbove(IntPtr window, IntPtr other)
    {
        if (window == IntPtr.Zero || other == IntPtr.Zero || window == other) return false;
        const uint PreviousWindow = 3; // GW_HWNDPREV
        for (IntPtr above = GetWindow(other, PreviousWindow); above != IntPtr.Zero; above = GetWindow(above, PreviousWindow))
            if (above == window) return true;
        return false;
    }

    public static bool NeedsTopmostRaise(IntPtr window, IntPtr mustBeAbove = default)
    {
        if (!IsTopmost(window)) return true;
        if (!GetWindowRect(window, out var bounds)) return false;

        const uint PreviousWindow = 3; // GW_HWNDPREV
        uint ownProcess = (uint)Environment.ProcessId;
        for (IntPtr above = GetWindow(window, PreviousWindow); above != IntPtr.Zero; above = GetWindow(above, PreviousWindow))
        {
            if (above == mustBeAbove) return true;
            if (!IsWindowVisible(above)) continue;
            GetWindowThreadProcessId(above, out uint processId);
            if (processId != ownProcess && GetWindowRect(above, out var other)
                && bounds.Left < other.Right && bounds.Right > other.Left
                && bounds.Top < other.Bottom && bounds.Bottom > other.Top)
                return true;
        }
        return false;
    }

    public static void SetTopmost(IntPtr h, bool enabled)
    {
        if (h == IntPtr.Zero || IsTopmost(h) == enabled) return;
        IntPtr insertAfter = new IntPtr(enabled ? -1 : -2); // HWND_TOPMOST / HWND_NOTOPMOST
        SetWindowPos(h, insertAfter, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
    }

    // Återställ fönstret längst fram i topmost-gruppen även om flaggan redan är satt.
    // SWP_NOACTIVATE gör att målapplikationen behåller tangentbordsfokus.
    public static void KeepOnTop(IntPtr h)
    {
        if (h == IntPtr.Zero) return;
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
    }

    public static void RaiseWithoutActivation(IntPtr h)
    {
        if (h != IntPtr.Zero)
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
    }

    static INPUT KeyInput(ushort scan, bool ext, ushort vk)
    {
        var i = new INPUT { type = 1 };
        i.u.ki.wVk = vk;
        i.u.ki.wScan = vk == 0 ? scan : (ushort)0;
        i.u.ki.dwFlags = (vk == 0 ? 8u : 0u) | (ext ? 1u : 0u);
        return i;
    }

    static INPUT ReleasedKey(INPUT input)
    {
        input.u.ki.dwFlags |= KeyUp;
        return input;
    }

    public static bool Key(ushort scan, bool ext, bool up, ushort vk = 0)
    {
        INPUT key = KeyInput(scan, ext, vk);
        return Send(new[] { up ? ReleasedKey(key) : key }) == 1;
    }

    public static bool PressKey(ushort scan, bool ext, ushort vk = 0)
    {
        INPUT key = KeyInput(scan, ext, vk);
        return SendPairs(new[] { key, ReleasedKey(key) });
    }

    public static bool ReplaceWord(int backspaces, string word)
    {
        var inputs = new INPUT[(backspaces + word.Length + 1) * 2];
        int index = 0;
        void AddKey(INPUT key)
        {
            inputs[index++] = key;
            inputs[index++] = ReleasedKey(key);
        }

        INPUT backspace = KeyInput(0x0E, false, 0);
        for (int i = 0; i < backspaces; i++) AddKey(backspace);
        foreach (char c in word) AddKey(UnicodeInput(c));
        AddKey(UnicodeInput(' '));
        return SendPairs(inputs);
    }

    public static bool InsertSavedText(ReadOnlySpan<byte> utf16, (IntPtr Active, IntPtr Focus) target, bool replaceValue = false)
    {
        if (target.Focus == IntPtr.Zero || utf16.Length == 0 || utf16.Length > 1024 || (utf16.Length & 1) != 0)
            return false;
        int selectionLength = replaceValue ? 4 : 0;
        var inputs = new INPUT[selectionLength + utf16.Length];
        try
        {
            if (replaceValue)
            {
                var control = KeyInput(0x1D, false, 0);
                var selectAll = KeyInput(0, false, 0x41);
                inputs[0] = control;
                inputs[1] = selectAll;
                inputs[2] = ReleasedKey(selectAll);
                inputs[3] = ReleasedKey(control);
            }
            for (int i = 0; i < utf16.Length; i += 2)
            {
                char character = (char)(utf16[i] | (utf16[i + 1] << 8));
                if (char.IsControl(character)) return false;
                int index = selectionLength + i;
                inputs[index] = UnicodeInput(character);
                inputs[index + 1] = ReleasedKey(inputs[index]);
            }
            if (InputTarget() != target || ModifierDown) return false;
            if (!replaceValue) return SendPairs(inputs);

            uint sent = Send(inputs);
            if (sent == inputs.Length) return true;
            if (sent == 2) Send(new[] { inputs[2], inputs[3] });
            else if (sent is 1 or 3) Send(new[] { inputs[3] });
            else if ((sent & 1) != 0) Send(new[] { inputs[sent] });
            return false;
        }
        finally { Array.Clear(inputs); }
    }

    static INPUT UnicodeInput(char c) => new() { type = 1, u = new() { ki = new() { wScan = c, dwFlags = 4 } } };

    // mode: 0 = vänsterklick, 1 = dubbelklick, 2 = högerklick
    public static bool Click(int mode)
    {
        uint down = mode == 2 ? 8u : 2u, up = mode == 2 ? 0x10u : 4u;
        int clicks = mode == 1 ? 2 : 1;
        var inputs = new INPUT[clicks * 2];
        for (int n = 0; n < clicks; n++)
        {
            inputs[n * 2] = MouseInput(down);
            inputs[n * 2 + 1] = MouseInput(up);
        }
        return SendPairs(inputs);
    }

    static INPUT MouseInput(uint flags) { var i = new INPUT { type = 0 }; i.u.mi.dwFlags = flags; return i; }
}
