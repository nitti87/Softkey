using System.Collections;
using System.Diagnostics;
using System.IO;
using Expression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

static class Program
{
    static int checks;
    static bool rejectModifierRelease;
    static int modifierCalls;
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static void Check(bool passed, string description)
    {
        if (!passed) throw new Exception(description);
        checks++;
        Console.WriteLine("PASS " + description);
    }

    static Process Child(string mode, string name)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(name);
        return Process.Start(start);
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--inspect-session")
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Desktop = DesktopContext.CurrentName, Locked = LockSession.IsLocked }));
            return 0;
        }
        if (args.Length > 0 && args[0].StartsWith("--"))
        {
            var instance = new SingleInstance(args[1]);
            if (args[0] == "--abandon") Environment.Exit(instance.IsOwner ? 0 : 2);
            bool owner = instance.IsOwner;
            instance.Dispose();
            return owner ? 0 : 3;
        }
        try
        {
            Storage();
            LockedSessionBoundary();
            Snapping();
            ToolbarGeometry();
            Instances();
            Interface(Path.GetFullPath(args[0]));
            LogonBoundary(Path.GetFullPath(args[0]));
            Positions(Path.GetFullPath(args[0]));
            Console.WriteLine($"All {checks} reliability checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static int nativeSendCalls;
    static uint lastMouseFlags;
    static uint SimulatedNativeSend(Array inputs)
    {
        nativeSendCalls++;
        var input = inputs.GetValue(0);
        if ((uint)input.GetType().GetField("type").GetValue(input) == 0)
        {
            var data = input.GetType().GetField("u").GetValue(input);
            var mouse = data.GetType().GetField("mi").GetValue(data);
            lastMouseFlags = (uint)mouse.GetType().GetField("dwFlags").GetValue(mouse);
        }
        return (uint)inputs.Length;
    }

    static void LockedSessionBoundary()
    {
        var prefix = new byte[20];
        Check(LockSession.Decode(prefix) == null, "unknown WTS information level fails closed");
        BitConverter.GetBytes(1).CopyTo(prefix, 0);
        Check(LockSession.Decode(prefix) == true, "Windows locked-session flag is decoded from the numeric prefix");
        BitConverter.GetBytes(1).CopyTo(prefix, 16);
        Check(LockSession.Decode(prefix) == false, "Windows unlocked-session flag is decoded correctly");
        BitConverter.GetBytes(-1).CopyTo(prefix, 16);
        Check(LockSession.Decode(prefix) == null, "unknown session flag does not authorize a lock-screen keyboard");
        Check(LockSession.Decode(Array.Empty<byte>()) == null, "truncated Windows session data fails closed");
        Check(LockSession.CanStartLockedKeyboard(false, "Default", true), "a locked user Default desktop may start its own restricted keyboard");
        Check(!LockSession.CanStartLockedKeyboard(false, "Default", false), "an unlocked desktop cannot start the restricted locked-screen keyboard");
        Check(!LockSession.CanStartLockedKeyboard(false, "Default", null), "unknown session state cannot open the locked-screen keyboard");
        Check(!LockSession.CanStartLockedKeyboard(true, "Default", true), "the user UIAccess helper cannot run as SYSTEM");
        Check(!LockSession.CanStartLockedKeyboard(false, "Winlogon", true), "the locked-desktop helper cannot replace the SYSTEM sign-in keyboard");

        // Replace the bottom-level sender before exercising any native input route.
        var field = typeof(Native).GetField("inputSender", BindingFlags.Static | BindingFlags.NonPublic);
        var original = field.GetValue(null);
        var input = Expression.Parameter(field.FieldType.GenericTypeArguments[0]);
        var simulated = Expression.Lambda(field.FieldType,
            Expression.Call(typeof(Program).GetMethod(nameof(SimulatedNativeSend), BindingFlags.Static | BindingFlags.NonPublic),
                Expression.Convert(input, typeof(Array))), input).Compile();
        bool previousActiveRequirement = Native.RequireActiveDesktop;
        field.SetValue(null, simulated);
        try
        {
            Native.RequireActiveDesktop = false;
            Native.AdditionalInputGuard = () => false;
            nativeSendCalls = 0;
            Check(!Native.PressKey(0x1E, false) && !Native.Click(0) && !Native.SetLeftMouseButton(true)
                && !Native.SetMiddleMouseButton(true) && !Native.SetRightMouseButton(true) && nativeSendCalls == 0,
                "the final session guard blocks keyboard, click and drag input before the native sender");
            Native.AdditionalInputGuard = () => true;
            Check(Native.PressKey(0x1E, false) && Native.Click(0) && Native.SetLeftMouseButton(true) && nativeSendCalls == 3,
                "authorized input reaches only the simulated native sender in the guard test");
            Check(Native.SetMiddleMouseButton(true) && lastMouseFlags == 0x20
                && Native.SetMiddleMouseButton(false) && lastMouseFlags == 0x40,
                "middle drag sends the Windows middle-button down and up flags, without real input");
            Check(Native.SetRightMouseButton(true) && lastMouseFlags == 0x08
                && Native.SetRightMouseButton(false) && lastMouseFlags == 0x10,
                "right drag sends the Windows right-button down and up flags, without real input");
        }
        finally
        {
            field.SetValue(null, original);
            Native.RequireActiveDesktop = previousActiveRequirement;
            Native.AdditionalInputGuard = null;
        }
    }

    static void Storage()
    {
        // Fault injection uses an isolated temporary folder, never the user's settings or words.
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-storage-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var store = new JsonFileStorage(folder);
        string path = Path.Combine(folder, "words.json");
        var first = new Dictionary<string, int> { ["äpple"] = 1 };
        var second = new Dictionary<string, int> { ["äpple"] = 2, ["ö"] = 3 };
        try
        {
            Check(store.Load("words.json", first) == first, "missing file returns the supplied default");
            Check(store.Save("words.json", first), "first generation can be saved");
            Check(store.Load<Dictionary<string, int>>("words.json", null)["äpple"] == 1, "Unicode words survive stream serialization");
            Check(store.Save("words.json", second), "second generation can replace the first");
            Check(JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path + ".bak")).Count == 1, "previous generation is retained as backup");
            File.WriteAllText(path, "{broken");
            Check(store.Load<Dictionary<string, int>>("words.json", null)["äpple"] == 1, "malformed primary recovers from backup");
            Check(File.ReadAllText(path) == "{broken", "loading does not destroy the damaged primary");
            Check(store.Save("words.json", second), "recovered data can be saved");
            Check(File.ReadAllText(Directory.GetFiles(folder, "*.corrupt-*").Single()) == "{broken", "damaged primary is preserved separately");
            Check(JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path + ".bak")).Count == 1, "corrupt primary never replaces the readable backup");
            File.WriteAllText(path, "null");
            Check(store.Load<Dictionary<string, int>>("words.json", null).Count == 1, "JSON null is treated as invalid rather than an empty word list");
            File.WriteAllText(path, "{\"äpple\":\"wrong type\"}");
            Check(store.Load<Dictionary<string, int>>("words.json", null).Count == 1, "invalid schema recovers from backup");
            File.Delete(path);
            Check(store.Load<Dictionary<string, int>>("words.json", null).Count == 1, "missing primary recovers from backup");
            Check(store.Save("words.json", second), "missing primary can be restored");
            byte[] before = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(!store.Save("words.json", first), "unreadable primary blocks overwrite");
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "failed save leaves the original bytes untouched");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            Check(!store.Save("words.json", first), "read-only destination reports save failure");
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "read-only save failure preserves data");
            File.SetAttributes(path, FileAttributes.Normal);
            Check(!store.Save<Dictionary<string, int>>("words.json", null), "null cannot overwrite saved data");
            Check(!store.Save("numbers.json", double.NaN), "serialization failure reports failure");
            Check(!Directory.GetFiles(folder, "*.tmp").Any(), "all failed saves clean up temporary files");
            File.WriteAllText(path, "broken");
            File.WriteAllText(path + ".bak", "broken too");
            Check(store.Load("words.json", first) == first, "both damaged generations return the supplied fallback");
            Check(File.ReadAllText(path) == "broken" && File.ReadAllText(path + ".bak") == "broken too", "fallback leaves damaged files available for recovery");
            var settings = new Settings { KeyboardPosition = new(-1200, 140), DragKeyPosition = new(300, 220) };
            Check(store.Save("settings.json", settings), "window positions can be saved");
            var restored = store.Load("settings.json", new Settings());
            Check(restored.KeyboardPosition == settings.KeyboardPosition && restored.DragKeyPosition == settings.DragKeyPosition, "both window positions survive a disk round trip, including negative coordinates");
            var legacy = JsonSerializer.Deserialize<Settings>("{\"PrivateTyping\":true,\"DwellMs\":900}");
            Check(legacy.DwellMs == 900 && legacy.KeyboardPosition == null && !JsonSerializer.Serialize(legacy).Contains("PrivateTyping"), "legacy settings keep working and the removed setting is discarded");
            var area = new Native.RECT { Left = -1920, Top = -200, Right = 0, Bottom = 880 };
            Check(WindowPositioning.Fit(new(-1500, -100), 288, 58, area) == new WindowPosition(-1500, -100), "valid negative-monitor position is preserved");
            Check(WindowPositioning.Fit(new(-100, -208), 288, 58, area) == new WindowPosition(-100, -208), "reachable overlap at monitor edges preserves the exact saved position");
            Check(WindowPositioning.Fit(new(int.MaxValue, int.MinValue), 288, 58, area) == new WindowPosition(-288, -200), "unavailable and extreme positions fit a visible work area without overflow");
            Check(WindowPositioning.Fit(new(100, 100), 3000, 2000, area) == new WindowPosition(-1920, -200), "oversized windows retain an accessible top-left corner");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(folder)) { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            Directory.Delete(folder);
        }
    }

    static void Instances()
    {
        string name = @"Local\SoftKey-check-" + Guid.NewGuid().ToString("N");
        using (var first = new SingleInstance(name))
        {
            Check(first.IsOwner, "first process owns the input-engine lock");
            using var second = Child("--probe", name);
            Check(second.WaitForExit(10000) && second.ExitCode == 3, "a separate process cannot acquire the same lock");
        }
        using (var next = Child("--probe", name))
            Check(next.WaitForExit(10000) && next.ExitCode == 0, "normal shutdown permits a subsequent launch");

        using var keepObjectAlive = new Mutex(false, name);
        using (var crashed = Child("--abandon", name))
            Check(crashed.WaitForExit(10000) && crashed.ExitCode == 0, "crash scenario leaves the named lock abandoned");
        using var recovered = new SingleInstance(name);
        Check(recovered.IsOwner, "abandoned lock permits restart after a crash");
    }

    static void Snapping()
    {
        var area = new Native.RECT { Left = 0, Top = 40, Right = 1920, Bottom = 1040 };
        Native.RECT Bounds(int top, int height = 300) => new() { Left = -80, Right = 780, Top = top, Bottom = top + height };
        Check(VerticalWindowSnap.Constrain(Bounds(50), area).Top == 40, "exactly 1% from the top snaps to the work-area edge");
        Check(VerticalWindowSnap.Constrain(Bounds(51), area).Top == 51, "outside the top threshold remains free");
        Check(VerticalWindowSnap.Constrain(Bounds(730), area).Bottom == 1040, "exactly 1% from the bottom snaps to the work-area edge");
        Check(VerticalWindowSnap.Constrain(Bounds(729), area).Top == 729, "outside the bottom threshold remains free");
        Check(VerticalWindowSnap.Constrain(Bounds(70), area).Top == 70, "the previous 3% zone no longer snaps");
        Check(VerticalWindowSnap.Constrain(Bounds(-500), area).Top == 40, "movement cannot cross the upper boundary");
        Check(VerticalWindowSnap.Constrain(Bounds(1500), area).Top == 740, "movement cannot cross the lower boundary");
        Check(VerticalWindowSnap.Constrain(Bounds(50, 50), area).Top == 40, "threshold depends on screen height rather than window height");
        var negativeArea = new Native.RECT { Left = -1920, Top = -1040, Right = 0, Bottom = -40 };
        Check(VerticalWindowSnap.Constrain(Bounds(-1030), negativeArea).Top == -1040, "negative monitor coordinates use their own top edge");
        var tallArea = new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1500 };
        Check(VerticalWindowSnap.Constrain(Bounds(15), tallArea).Top == 0 && VerticalWindowSnap.Constrain(Bounds(16), tallArea).Top == 16, "1% threshold scales with the monitor work area");
        Check(VerticalWindowSnap.Constrain(Bounds(50), area, true).Top == 50, "resizing does not trigger movement snapping");
        var resizedTop = VerticalWindowSnap.Constrain(Bounds(20, 400), area, true);
        Check(resizedTop.Top == 40 && resizedTop.Bottom == 420, "top-edge resize stops at the boundary and anchors the bottom");
        var resizedBottom = VerticalWindowSnap.Constrain(Bounds(800, 400), area, true);
        Check(resizedBottom.Top == 800 && resizedBottom.Bottom == 1040, "bottom-edge resize stops at the boundary and anchors the top");
        var oversized = VerticalWindowSnap.Constrain(Bounds(-200, 1800), area);
        Check(oversized.Top == 40 && oversized.Bottom == 1040, "oversized window fits the available vertical space");
        var outsideRight = new Native.RECT { Left = 1800, Right = 2660, Top = 100, Bottom = 400 };
        var rightBound = VerticalWindowSnap.Constrain(outsideRight, area);
        Check(rightBound.Left == 1060 && rightBound.Right == 1920, "right boundary preserves the complete window width");
        var nearLeft = new Native.RECT { Left = 5, Right = 865, Top = 100, Bottom = 400 };
        Check(VerticalWindowSnap.Constrain(nearLeft, area).Left == 5, "horizontal edges are limits without a magnetic snap zone");
        var leftResize = VerticalWindowSnap.Constrain(Bounds(100), area, true);
        Check(leftResize.Left == 0 && leftResize.Right == 780, "left resize anchors the opposite edge");
        var rightResize = VerticalWindowSnap.Constrain(outsideRight, area, true);
        Check(rightResize.Left == 1800 && rightResize.Right == 1920, "right resize anchors the opposite edge");
        var tooWide = new Native.RECT { Left = -300, Right = 2500, Top = 100, Bottom = 400 };
        var fitWide = VerticalWindowSnap.Constrain(tooWide, area);
        Check(fitWide.Left == area.Left && fitWide.Right == area.Right, "oversized window fits the available horizontal space");
        bool stable = true;
        for (int y = -1500; y <= 2000; y += 7)
        {
            var result = VerticalWindowSnap.Constrain(Bounds(y), area);
            var again = VerticalWindowSnap.Constrain(result, area);
            stable &= result.Top >= area.Top && result.Bottom <= area.Bottom
                && result.Bottom - result.Top == 300 && result.Left == 0 && result.Right == 860
                && again.Top == result.Top && again.Bottom == result.Bottom;
        }
        Check(stable, "repeated snapping is stable and preserves window size within all four boundaries");
    }

    static void ToolbarGeometry()
    {
        var area = new Native.RECT { Left = 20, Top = 40, Right = 1920, Bottom = 1040 };
        Native.RECT Bounds(int x, int y, int width = 288, int height = 58) => new() { Left = x, Top = y, Right = x + width, Bottom = y + height };
        bool Same(Native.RECT a, Native.RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        Check(DragKeyScreenBounds.Constrain(Bounds(-100, 100), area).Left == area.Left, "DragKey is bounded at the left work-area edge");
        Check(DragKeyScreenBounds.Constrain(Bounds(2000, 100), area).Right == area.Right, "DragKey is bounded at the right work-area edge");
        Check(DragKeyScreenBounds.Constrain(Bounds(100, -100), area).Top == area.Top, "DragKey is bounded at the top work-area edge");
        Check(DragKeyScreenBounds.Constrain(Bounds(100, 1100), area).Bottom == area.Bottom, "DragKey stays above the taskbar/bottom work-area edge");
        foreach (var point in new[] { (-100, -100), (2000, -100), (-100, 1100), (2000, 1100) })
        {
            var result = DragKeyScreenBounds.Constrain(Bounds(point.Item1, point.Item2), area);
            Check(result.Left >= area.Left && result.Top >= area.Top && result.Right <= area.Right && result.Bottom <= area.Bottom
                && result.Right - result.Left == 288 && result.Bottom - result.Top == 58, "a DragKey corner overrun keeps the whole panel visible at its original size");
        }
        var nearEdge = Bounds(21, 41);
        Check(Same(DragKeyScreenBounds.Constrain(nearEdge, area), nearEdge), "DragKey has hard boundaries without SoftKey's magnetic edge snapping");
        var negative = new Native.RECT { Left = -1920, Top = -1080, Right = 0, Bottom = -40 };
        var negativeResult = DragKeyScreenBounds.Constrain(Bounds(-2000, -1200), negative);
        Check(negativeResult.Left == -1920 && negativeResult.Top == -1080 && negativeResult.Right - negativeResult.Left == 288,
            "DragKey bounds use physical coordinates on monitors left of and above the primary monitor");
        Check(Same(DragKeyScreenBounds.Constrain(Bounds(-2000, -1200, 6000, 4000), negative), negative),
            "a panel larger than the available work area is fitted completely inside it");
        var compact = DragKeyScreenBounds.Constrain(Bounds(1910, 1030, 80, 38), area);
        Check(compact.Right == area.Right && compact.Bottom == area.Bottom && compact.Right - compact.Left == 80,
            "the compact sign-in toolbar uses its own actual dimensions");
        var extreme = DragKeyScreenBounds.Constrain(Bounds(int.MinValue, int.MinValue), negative);
        Check(extreme.Left == negative.Left && extreme.Top == negative.Top, "extreme saved negative positions are clamped without arithmetic overflow");
        bool stable = true;
        for (int x = -2200; x <= 2100; x += 71)
        {
            var result = DragKeyScreenBounds.Constrain(Bounds(x, x / 2), area);
            stable &= Same(result, DragKeyScreenBounds.Constrain(result, area));
        }
        Check(stable, "repeated DragKey boundary checks do not move an already constrained panel");
    }

    static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Members).SetValue(target, value);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Members).Invoke(target, args);
    static bool FakeModifier(object key, bool up) { modifierCalls++; return up && !rejectModifierRelease; }

    static void Interface(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType("KeyboardWindow", true);
        // No window is shown and all timers are stopped before pumping messages. No input is sent.
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-interface-check-" + Guid.NewGuid().ToString("N"));
        var store = Activator.CreateInstance(assembly.GetType("JsonFileStorage"), folder);
        var window = (Window)Activator.CreateInstance(type, store);
        foreach (string timer in new[] { "dwellTimer", "topmostTimer", "keyFeedbackTimer" }) ((DispatcherTimer)Get(window, timer)).Stop();
        object cfg = Get(window, "cfg");
        cfg.GetType().GetProperty("AutoClick").SetValue(cfg, false);
        Check(cfg.GetType().GetProperty("PrivateTyping") == null && type.GetMethod("TogglePrivateTyping", Members) == null, "private typing setting and action are completely removed");
        Set(window, "statusMessage", null);
        Set(window, "currentWord", "kontrollord");
        Set(window, "wordTarget", (new IntPtr(1), new IntPtr(1)));
        var words = (IDictionary)Get(window, "words");
        Call(window, "Commit");
        Check(words.Contains("kontrollord"), "ordinary word learning remains available");
        Set(window, "currentWord", "kontroll");
        Call(window, "RefreshWordFeedback");
        Check(((TextBlock)Get(window, "statusText")).Text == "kontroll", "status row shows the word as before");
        Check(((Button[])Get(window, "sugg")).Any(b => b.Visibility == Visibility.Visible && (string)b.Content == "kontrollord"), "ordinary predictions remain available");

        var buttons = (IDictionary)Get(window, "btn");
        object key = buttons.Keys.Cast<object>().First(k => (string)k.GetType().GetProperty("Label").GetValue(k) == "Sft");
        var menu = (System.Windows.Controls.Panel)Call(window, "BuildMenuPanel", 1);
        Check(menu.Children.OfType<Button>().Select(button => button.Content).SequenceEqual(new object[] { "Inställningar…", "Ordlista…" })
            && !menu.Children.OfType<CheckBox>().Any(), "the keyboard menu offers settings and the combined word list");

        var input = type.GetField("modifierInput", Members);
        var keyParameter = Expression.Parameter(key.GetType());
        var upParameter = Expression.Parameter(typeof(bool));
        var fake = Expression.Lambda(input.FieldType, Expression.Call(typeof(Program).GetMethod(nameof(FakeModifier), BindingFlags.NonPublic | BindingFlags.Static),
            Expression.Convert(keyParameter, typeof(object)), upParameter), keyParameter, upParameter).Compile();
        input.SetValue(window, fake);
        object held = Get(window, "held"), locked = Get(window, "lockedModifiers");
        Call(held, "Add", key); Call(locked, "Add", key);
        rejectModifierRelease = true;
        Check(!(bool)Call(window, "ReleaseHeldModifiers"), "rejected modifier release reports failure");
        Check((bool)Call(held, "Contains", key) && (bool)Call(locked, "Contains", key), "rejected release retains ownership and lock state");
        rejectModifierRelease = false;
        Check((bool)Call(window, "ReleaseHeldModifiers") && !(bool)Call(held, "Contains", key) && !(bool)Call(locked, "Contains", key), "successful retry releases the modifier and clears state");
        Check(modifierCalls == 2, "release test used only simulated input calls");

        Call(window, "OutsideClickHook", -1, IntPtr.Zero, IntPtr.Zero);
        Call(window, "OutsideClickHook", 0, new IntPtr(0x200), IntPtr.Zero);
        Check(true, "native hook safely forwards negative codes and missing data");
        object originalActions = Get(window, "mouseActions");
        Set(window, "mouseActions", null);
        Call(window, "OutsideClickHook", 0, new IntPtr(0x200), new IntPtr(1));
        Check(true, "managed callback failure is contained before native marshalling");
        Set(window, "mouseActions", originalActions);

        var resilienceType = assembly.GetType("SoftKeyResilience", true);
        var isFatal = resilienceType.GetMethod("IsFatal", BindingFlags.Static | BindingFlags.NonPublic);
        Check((bool)isFatal.Invoke(null, new object[] { new OutOfMemoryException() })
            && (bool)isFatal.Invoke(null, new object[] { new BadImageFormatException() })
            && (bool)isFatal.Invoke(null, new object[] { new AggregateException(new OutOfMemoryException()) }),
            "resilience guard treats fatal CLR and aggregate errors as unrecoverable");
        Check(!(bool)isFatal.Invoke(null, new object[] { new COMException("transient", -2147467259) })
            && !(bool)isFatal.Invoke(null, new object[] { new InvalidOperationException("transient") })
            && !(bool)isFatal.Invoke(null, new object[] { new IOException("transient") }),
            "resilience guard treats transient COM, UI and I/O errors as recoverable");

        long fakeClock = 1_000;
        var recoveredTypes = new List<string>();
        var guard = Activator.CreateInstance(resilienceType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { (Action<string>)(name => recoveredTypes.Add(name)), (Func<long>)(() => fakeClock) }, null);
        var tryRecover = resilienceType.GetMethod("TryRecover", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < 5; i++)
        {
            Check((bool)tryRecover.Invoke(guard, new object[] { new InvalidOperationException("transient") }),
                $"resilience guard recovers transient dispatcher error {i + 1} of 5");
        }
        Check(!(bool)tryRecover.Invoke(guard, new object[] { new InvalidOperationException("storm") }),
            "resilience guard lets an unbroken error storm exit so crash recovery can restart cleanly");
        fakeClock += 10_000;
        Check((bool)tryRecover.Invoke(guard, new object[] { new COMException("after-window") })
            && recoveredTypes.Count == 6 && recoveredTypes[^1] == nameof(COMException),
            "resilience guard resets its error budget after the burst window elapses");
        int countBeforeFatal = recoveredTypes.Count;
        Check(!(bool)tryRecover.Invoke(guard, new object[] { new OutOfMemoryException() })
            && recoveredTypes.Count == countBeforeFatal,
            "resilience guard refuses recovery on fatal exceptions without invoking recovery callbacks");
        var throwingGuard = Activator.CreateInstance(resilienceType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { (Action<string>)(_ => throw new InvalidOperationException("callback failed")), (Func<long>)(() => fakeClock) }, null);
        Check((bool)tryRecover.Invoke(throwingGuard, new object[] { new IOException("io") }),
            "resilience guard survives a failing recovery callback");

        Set(window, "checkingDisplayedWord", true);
        Set(window, "emailInsertion", new System.Threading.CancellationTokenSource());
        Set(window, "openMenu", 1);
        Call(window, "RecoverFromTransientError");
        Check(!(bool)Get(window, "checkingDisplayedWord")
            && Get(window, "emailInsertion") == null
            && (int)Get(window, "openMenu") == -1
            && words.Contains("kontrollord"),
            "transient error recovery resets interaction state without altering personal words");

        var progressViewType = assembly.GetType("SoftKeyUpdateProgressView", true);
        string progressXaml = (string)progressViewType.GetMethod("InstallationContent").Invoke(null, new object[] { 488.0 });
        var updatesType = assembly.GetType("SoftKeyUpdates", true);
        var resMethod = updatesType.GetMethod("Resource", BindingFlags.Static | BindingFlags.NonPublic);
        var paramMethod = updatesType.GetMethod("Parameters", BindingFlags.Static | BindingFlags.NonPublic);
        var startInfoMethod = updatesType.GetMethod("StartInfo", BindingFlags.Static | BindingFlags.NonPublic);
        string fullInstallScript = (string)paramMethod.Invoke(null, new object[]
        {
            new
            {
                PackagePath = @"C:\Users\nitti\AppData\Local\SoftKey\Updates\SoftKey-update-5.zip",
                BuildNumber = 5L,
                Commit = new string('a', 40),
                InstalledBuild = 0L,
                DesktopProcess = 1234,
                DesktopStarted = 123456L,
                ProgressContent = progressXaml,
                Left = 100.0,
                Top = 100.0
            }
        })
            + (string)resMethod.Invoke(null, new object[] { "SoftKey.UpdatePackage.ps1" })
            + (string)resMethod.Invoke(null, new object[] { "SoftKey.InstallProgress.ps1" })
            + "\nInvoke-SoftKeyInstallWithProgress {\n"
            + (string)resMethod.Invoke(null, new object[] { "SoftKey.ApplyUpdate.ps1" })
            + "\n}\n";
        var installPsi = (ProcessStartInfo)startInfoMethod.Invoke(null, new object[] { fullInstallScript });
        Check(installPsi.Arguments.Length < 16_000,
            $"update install worker command stays well below Windows' limit ({installPsi.Arguments.Length} chars)");

        string roundTripScript = (string)paramMethod.Invoke(null, new object[] { new { Text = "åäö • test 'quote'" } })
            + "Write-Output $updateParameters.Text";
        var roundTripPsi = (ProcessStartInfo)startInfoMethod.Invoke(null, new object[] { roundTripScript });
        roundTripPsi.RedirectStandardOutput = true;
        roundTripPsi.StandardOutputEncoding = System.Text.Encoding.UTF8;
        using (var probe = Process.Start(roundTripPsi))
        {
            string stdout = probe.StandardOutput.ReadToEnd().Trim();
            probe.WaitForExit(10_000);
            Check(probe.ExitCode == 0 && stdout == "åäö • test 'quote'",
                "compressed PowerShell launcher preserves UTF-8 text and quotes");
        }

        Set(window, "closeConfirmed", true);
        window.Close();
        foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
        Directory.Delete(folder);
    }

    static int signInKeyCalls;
    static bool FakeSignInKey(object key) { signInKeyCalls++; return true; }

    static void LogonBoundary(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType("KeyboardWindow", true);
        // A null store makes any accidental access to personal settings or words fail immediately.
        var cfg = Activator.CreateInstance(assembly.GetType("Settings", true));
        var window = (Window)Activator.CreateInstance(type, new object[] { null, true, cfg, false });
        foreach (string timer in new[] { "dwellTimer", "topmostTimer", "keyFeedbackTimer", "positionSaveTimer" }) ((DispatcherTimer)Get(window, timer)).Stop();
        try
        {
            var words = (IDictionary)Get(window, "words");
            Check(words.Count == 0, "sign-in never loads a personal or built-in word list");
            Set(window, "inputDesktopActive", (Func<bool>)(() => true));
            var input = type.GetField("keyInput", Members);
            var keyType = input.FieldType.GenericTypeArguments[0];
            var parameter = Expression.Parameter(keyType);
            input.SetValue(window, Expression.Lambda(input.FieldType,
                Expression.Call(typeof(Program).GetMethod(nameof(FakeSignInKey), BindingFlags.NonPublic | BindingFlags.Static),
                    Expression.Convert(parameter, typeof(object))), parameter).Compile());
            var buttons = (IDictionary)Get(window, "btn");
            object Key(string label) => buttons.Keys.Cast<object>().First(k => (string)k.GetType().GetProperty("Label").GetValue(k) == label);
            signInKeyCalls = 0;
            foreach (string label in new[] { "a", "b", "1", "BkSp", "Enter" })
            {
                Call(window, "Press", Key(label));
                Check((string)Get(window, "currentWord") == "" && words.Count == 0, "sign-in key " + label + " creates no word buffer or learned word");
            }
            Check(signInKeyCalls == 5, "sign-in sends keys through the input engine using only simulated events in this test");
            Set(window, "inputDesktopActive", (Func<bool>)(() => false));
            Call(window, "Press", Key("a"));
            Check(signInKeyCalls == 5, "sign-in sends no key when its desktop is inactive");
            int mouseCalls = 0;
            var simulatedMouse = Activator.CreateInstance(assembly.GetType("MouseActionState"),
                (Func<int, bool>)(_ => { mouseCalls++; return true; }),
                (Func<bool, bool>)(_ => { mouseCalls++; return true; }),
                (Func<bool, bool>)(_ => { mouseCalls++; return true; }),
                (Func<bool, bool>)(_ => { mouseCalls++; return true; }));
            Set(window, "mouseActions", simulatedMouse);
            Call(window, "PerformDwell", Enum.Parse(assembly.GetType("DwellTarget"), "External"));
            Check(mouseCalls == 0 && signInKeyCalls == 5, "inactive-desktop dwell returns without injecting input");
            Set(window, "currentWord", "dummytest");
            Call(window, "Track", Key("a"), true, "a");
            Call(window, "LearnWord", "dummytest");
            Check((string)Get(window, "currentWord") == "" && words.Count == 0, "direct tracking and learning entry points also reject sign-in data");
            Check(!(bool)Call(window, "UpdateWordList", Activator.CreateInstance(words.GetType())), "sign-in cannot persist a word list");
            Call(window, "OpenWordList");
            Check(Get(window, "wordListWin") == null, "personal word management cannot open at sign-in");
            Call(window, "SaveSettings");
            Check(Get(window, "storage") == null, "saving sign-in preferences never opens a personal data store");
            Check(((TextBlock)Get(window, "statusText")).Text == "Inloggning", "sign-in status contains no typed word");
            Check(((Button[])Get(window, "sugg")).All(b => b.Visibility == Visibility.Collapsed), "all sign-in word suggestions stay hidden");
            Check(((Image)Get(window, "emptySuggestionsIcon")).Visibility == Visibility.Collapsed, "sign-in does not display the prediction placeholder");
            var headers = (Button[])Get(window, "menuHeaders");
            Check(headers.All(b => b.Visibility == Visibility.Collapsed),
                "sign-in hides every menu, including mouse settings, while retaining its separate autoclick control");
            type.GetMethod("OpenMenu", Members, null, new[] { typeof(int), typeof(Rect) }, null)
                .Invoke(window, new object[] { 1, new Rect() });
            Check((int)Get(window, "openMenu") == -1, "hidden sign-in menus cannot be opened programmatically");
            bool alwaysOnTop = (bool)cfg.GetType().GetProperty("AlwaysOnTop").GetValue(cfg);
            Call(window, "ToggleAlwaysOnTop");
            Check(alwaysOnTop && window.Topmost, "sign-in keyboard cannot disable its always-on-top setting");
            Call(window, "OpenDragKey");
            ((DispatcherTimer)Get(window, "dwellTimer")).Stop();
            object toolbar = Get(window, "dragKeyWin");
            var toolbarButtons = (System.Windows.Controls.Primitives.UniformGrid)Get(toolbar, "buttons");
            Check(toolbarButtons.Children.Count == 1 && toolbarButtons.Children[0] == Get(toolbar, "autoClick"),
                "the sign-in DragKey visual tree contains only the autoclick button");
            Check(toolbarButtons.Columns == 1 && ((FrameworkElement)toolbar.GetType().GetProperty("Surface").GetValue(toolbar)).Width == 80,
                "the single sign-in autoclick button uses a compact toolbar without empty button slots");
            Check(System.Windows.Automation.AutomationProperties.GetItemStatus((DependencyObject)Get(toolbar, "autoClick")) == "På",
                "the sign-in autoclick button initially reports On");
            Call(window, "ToggleRightClick"); Call(window, "ToggleDoubleClick");
            var actionType = assembly.GetType("MouseAction");
            Call(window, "ToggleDrag", Enum.Parse(actionType, "Drag"));
            Call(window, "ToggleDrag", Enum.Parse(actionType, "MiddleDrag"));
            Call(window, "ToggleDrag", Enum.Parse(actionType, "RightDrag"));
            Call(window, "OpenDragKeySettings");
            Check(Get(window, "dragKeySettingsWin") == null, "sign-in cannot open the Dragkey settings dialog");
            var actionState = Get(window, "mouseActions");
            Check(actionState.GetType().GetProperty("Selected").GetValue(actionState).ToString() == "Left",
                "removed sign-in mouse actions cannot be armed through their handlers");
            Call(window, "ToggleAutoClick");
            Check(!(bool)cfg.GetType().GetProperty("AutoClick").GetValue(cfg), "the sole sign-in button can still pause autoclick");
            Call(window, "ToggleAutoClick");
            ((DispatcherTimer)Get(window, "dwellTimer")).Stop();
            Check((bool)cfg.GetType().GetProperty("AutoClick").GetValue(cfg) && Get(window, "storage") == null,
                "the same button resumes autoclick without opening or changing personal storage");
        }
        finally
        {
            Set(window, "closeConfirmed", true);
            window.Close();
        }

        string directory = Path.Combine(Path.GetTempPath(), "SoftKey-logon-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "logon-settings.json");
        try
        {
            Settings ReadFile() => LogonSettings.LoadFile(directory).ToSettings();
            Check(ReadFile().AutoClick, "missing sign-in configuration starts dwell with defaults");
            File.WriteAllText(path, "{\"DwellMs\":1,\"JitterPx\":9999,\"WindowWidth\":99999,\"WindowHeight\":1,\"AutoClick\":false,\"Repeat\":true,\"Words\":\"ignored\"}");
            var settings = ReadFile();
            Check(settings.DwellMs == Settings.MinDwellMs && settings.JitterPx == Settings.MaxJitterPx, "sign-in dwell preferences are bounded");
            Check(settings.WindowWidth == 1600 && settings.WindowHeight == 240, "sign-in window dimensions are bounded");
            Check(settings.AutoClick && !settings.Repeat && settings.AlwaysOnTop, "unsupported JSON fields cannot disable dwell or enable repeated input");
            new AutoclickTimingSettings(750, 35).ApplyTo(settings);
            Check(settings.DwellMs == 750 && settings.JitterPx == 35 && settings.AutoClick && !settings.Repeat
                && settings.WindowWidth == 1600 && settings.WindowHeight == 240, "the shared dwell values replace only sign-in timing");
            File.WriteAllText(path, "{\"StartPaused\":true}");
            Check(ReadFile().AutoClick, "the legacy paused pilot setting cannot disable autoclick at sign-in");
            File.WriteAllText(path, "{broken");
            Check(ReadFile().DwellMs == 800, "malformed sign-in settings use working defaults");
            File.WriteAllText(path, new string(' ', 5000));
            Check(ReadFile().DwellMs == 800, "oversized sign-in settings are rejected before deserialization");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    static void Pump(int milliseconds = 50)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    static void Positions(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        string folder = Path.Combine(Path.GetTempPath(), "SoftKey-position-check-" + Guid.NewGuid().ToString("N"));
        var disk = new JsonFileStorage(folder);
        Check(disk.Save("settings.json", new Settings { AutoClick = false, AlwaysOnTop = false, DragKeyPlacement = DragKeyPlacement.Floating }), "isolated startup settings disable real autoclick");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window window = null;
        Window Create()
        {
            var store = Activator.CreateInstance(assembly.GetType("JsonFileStorage"), folder);
            var result = (Window)Activator.CreateInstance(assembly.GetType("KeyboardWindow"), store);
            result.Loaded += (_, _) =>
            {
                ((DispatcherTimer)Get(result, "dwellTimer")).Stop();
                // The position checks supply their own pointer updates.
                Call(result, "DisposeMenuDismissHook");
            };
            result.Show(); Pump();
            return result;
        }
        WindowPosition Position(Window target) => WindowPositioning.Capture(new System.Windows.Interop.WindowInteropHelper(target).Handle);
        WindowPosition ConfigPosition(string name)
        {
            object position = Get(window, "cfg").GetType().GetProperty(name).GetValue(Get(window, "cfg"));
            return position == null ? null : new WindowPosition((int)position.GetType().GetProperty("X").GetValue(position), (int)position.GetType().GetProperty("Y").GetValue(position));
        }
        void Close()
        {
            Set(window, "closeConfirmed", true);
            window.Close(); Pump();
        }
        void DwellMove(Window target)
        {
            string settingName = target == window ? "KeyboardPosition" : "DragKeyPosition";
            var diskBefore = disk.Load("settings.json", new Settings());
            object cfg = Get(window, "cfg"), actions = Get(window, "mouseActions");
            cfg.GetType().GetProperty("AutoClick").SetValue(cfg, true);
            var actionType = assembly.GetType("MouseAction");
            Call(actions, "Select", Enum.Parse(actionType, "Drag"));
            var pointType = assembly.GetType("Native+POINT");
            object Point(int x, int y)
            {
                object p = Activator.CreateInstance(pointType);
                pointType.GetField("X").SetValue(p, x); pointType.GetField("Y").SetValue(p, y);
                return p;
            }
            var initial = Position(target);
            Check((bool)Call(window, "BeginWindowDrag", target, Point(initial.X + 10, initial.Y + 10)), "own-window dwell move begins without mouse-button injection");
            ((DispatcherTimer)Get(window, "windowDragFallbackTimer")).Stop();
            cfg.GetType().GetProperty("AutoClick").SetValue(cfg, false);
            Check((bool)Call(window, "UpdateWindowDrag", Point(initial.X + 30, initial.Y + 30)) && Position(target) != initial,
                "own-window dwell move reaches the new position");
            Pump(450);
            var diskDuring = disk.Load("settings.json", new Settings());
            Check((settingName == "KeyboardPosition" ? diskDuring.KeyboardPosition == diskBefore.KeyboardPosition : diskDuring.DragKeyPosition == diskBefore.DragKeyPosition), "active dwell move defers disk writes until release");
            if (target != window)
                Check(Get(window, "dragKeyPreviewPlacement").ToString() == nameof(DragKeyPlacement.Floating),
                    "the floating DragKey persistence check releases outside the docking zones");
            Check((bool)Call(window, "FinishWindowDrag"), "finishing dwell move saves the new position");
        }
        void ToolbarScreenEdges(Window toolbar)
        {
            var toolbarHandle = new System.Windows.Interop.WindowInteropHelper(toolbar).Handle;
            Native.RECT Bounds() { Native.GetWindowRect(toolbarHandle, out var result); return result; }
            bool Same(Native.RECT a, Native.RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
            var original = Bounds();
            WindowPositioning.TryWorkArea(original, out var area);
            int width = original.Right - original.Left, height = original.Bottom - original.Top;
            Native.RECT Proposed(int x, int y) => new() { Left = x, Top = y, Right = x + width, Bottom = y + height };
            var candidates = new[] {
                Proposed(area.Left - 200, area.Top + 90), Proposed(area.Right + 200, area.Top + 90),
                Proposed(area.Left + 90, area.Top - 200), Proposed(area.Left + 90, area.Bottom + 200),
                Proposed(area.Right + 200, area.Bottom + 200) };
            var focus = Native.ForegroundWindow;
            foreach (var candidate in candidates)
            {
                var expected = DragKeyScreenBounds.ConstrainToWorkArea(candidate);
                Native.MoveWithoutActivation(toolbarHandle, candidate.Left, candidate.Top);
                Check(Same(Bounds(), expected), "a real native DragKey move is constrained to the selected monitor's complete work area");
            }
            IntPtr rectangle = Marshal.AllocHGlobal(Marshal.SizeOf<Native.RECT>());
            try
            {
                var proposed = candidates[^1];
                Marshal.StructureToPtr(proposed, rectangle, false);
                Call(toolbar, "FilterWindowMessages", toolbarHandle, 0x0216, IntPtr.Zero, rectangle, false);
                Check(Same(Marshal.PtrToStructure<Native.RECT>(rectangle), DragKeyScreenBounds.ConstrainToWorkArea(proposed)),
                    "ordinary mouse dragging constrains the WM_MOVING rectangle before displaying the docking preview");
            }
            finally { Marshal.FreeHGlobal(rectangle); }

            // Exercise the dwell path without sending any physical mouse-button events.
            object cfg = Get(window, "cfg"), actions = Get(window, "mouseActions");
            cfg.GetType().GetProperty("AutoClick").SetValue(cfg, true);
            Call(actions, "Select", Enum.Parse(assembly.GetType("MouseAction"), "Drag"));
            var pointType = assembly.GetType("Native+POINT");
            object Point(int x, int y)
            {
                object point = Activator.CreateInstance(pointType);
                pointType.GetField("X").SetValue(point, x); pointType.GetField("Y").SetValue(point, y);
                return point;
            }
            var start = Bounds();
            Check((bool)Call(window, "BeginWindowDrag", toolbar, Point(start.Left + 10, start.Top + 10)), "the bounded DragKey dwell test starts an own-window move");
            ((DispatcherTimer)Get(window, "windowDragFallbackTimer")).Stop();
            cfg.GetType().GetProperty("AutoClick").SetValue(cfg, false);
            foreach (var candidate in candidates)
            {
                Call(window, "UpdateWindowDrag", Point(candidate.Left + 10, candidate.Top + 10));
                Check(Same(Bounds(), DragKeyScreenBounds.ConstrainToWorkArea(candidate)), "dwell dragging keeps the entire DragKey inside the monitor work area");
            }
            Call(window, "CancelMouseAction");
            Check(Native.ForegroundWindow == focus, "DragKey edge enforcement never takes typing focus");

            // A partially offscreen bookmark used to be accepted by the general window restorer.
            var legacy = Proposed(area.Right - 100, area.Bottom - 25);
            Check(WindowPositioning.Fit(new WindowPosition(legacy.Left, legacy.Top), width, height, area) == new WindowPosition(legacy.Left, legacy.Top),
                "the legacy-bookmark sample really was accepted by the previous partial-visibility rule");
            Check(WindowPositioning.Restore(toolbarHandle, new WindowPosition(legacy.Left, legacy.Top)), "a legacy DragKey bookmark can be restored");
            Check(Same(Bounds(), DragKeyScreenBounds.ConstrainToWorkArea(legacy)), "restoring a partly offscreen DragKey bookmark brings the complete panel back into view");
        }
        void EdgeSnapping(Window toolbar)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Native.RECT Bounds() { Native.GetWindowRect(handle, out var result); return result; }
            var original = Bounds();
            Check(WindowPositioning.TryWorkArea(original, out var area), "snapping reads the actual monitor work area");
            int threshold = (int)(((long)area.Bottom - area.Top) / 100);
            int height = original.Bottom - original.Top, width = original.Right - original.Left;
            int x = area.Left + 90;
            var focus = Native.ForegroundWindow;
            Native.MoveWithoutActivation(handle, x, area.Top + threshold);
            Check(Bounds().Top == area.Top && Bounds().Left == x, "actual native move snaps the top without changing horizontal position");
            Native.MoveWithoutActivation(handle, x, area.Top + threshold + 3);
            Check(Bounds().Top == area.Top + threshold + 3, "moving away releases the top snap without sticking");
            Native.ResizeWithoutActivation(handle, x, area.Top + threshold, width, height);
            Check(Bounds().Top == area.Top, "move with explicit unchanged size also triggers the snap");
            Native.MoveWithoutActivation(handle, x, area.Bottom - height - threshold);
            Check(Bounds().Bottom == area.Bottom, "actual native move snaps the bottom");
            Native.MoveWithoutActivation(handle, x, area.Bottom + 100);
            Check(Bounds().Bottom == area.Bottom, "native movement cannot put the window below the work area");
            Native.MoveWithoutActivation(handle, x, area.Top - 100);
            Check(Bounds().Top == area.Top, "native movement cannot put the window above the work area");
            Native.MoveWithoutActivation(handle, area.Left - 200, area.Top + 100);
            Check(Bounds().Left == area.Left && Bounds().Right - Bounds().Left == width, "native movement cannot put SoftKey beyond the left edge");
            Native.MoveWithoutActivation(handle, area.Right + 200, area.Top + 100);
            Check(Bounds().Right == area.Right && Bounds().Right - Bounds().Left == width, "native movement cannot put SoftKey beyond the right edge");
            Native.MoveWithoutActivation(handle, area.Left + 5, area.Top + 100);
            Check(Bounds().Left == area.Left + 5, "native movement within the horizontal boundary remains free");
            Native.MoveWithoutActivation(new System.Windows.Interop.WindowInteropHelper(toolbar).Handle, x, area.Top + threshold);
            Check(Position(toolbar).Y == area.Top + threshold, "floating DragKey retains its independent free movement");
            Check(Native.ForegroundWindow == focus, "snapping and boundary enforcement preserve input focus");

            IntPtr rectangle = Marshal.AllocHGlobal(Marshal.SizeOf<Native.RECT>());
            try
            {
                var proposed = new Native.RECT { Left = x, Right = x + width, Top = area.Top + threshold, Bottom = area.Top + threshold + height };
                Marshal.StructureToPtr(proposed, rectangle, false);
                Call(window, "FilterMainWindowCommands", handle, 0x0216, IntPtr.Zero, rectangle, false);
                Check(Marshal.PtrToStructure<Native.RECT>(rectangle).Top == area.Top, "manual WM_MOVING outline uses the same top snap");
            }
            finally { Marshal.FreeHGlobal(rectangle); }

            Native.ResizeWithoutActivation(handle, x, area.Top + 100, width, height);
            int anchoredBottom = Bounds().Bottom;
            Native.ResizeWithoutActivation(handle, x, area.Top - 50, width, anchoredBottom - area.Top + 50);
            Check(Bounds().Top == area.Top && Bounds().Bottom == anchoredBottom, "actual top resize respects the limit without moving the opposite edge");
            Native.ResizeWithoutActivation(handle, x, area.Top + 100, width, height);
            Native.ResizeWithoutActivation(handle, x, area.Top + 100, width, area.Bottom - area.Top);
            Check(Bounds().Bottom == area.Bottom && Bounds().Top == area.Top + 100, "actual bottom resize respects the limit without moving the opposite edge");
            Native.ResizeWithoutActivation(handle, x, area.Top + 100, width, height);

            int anchoredRight = Bounds().Right;
            Native.ResizeWithoutActivation(handle, area.Left - 50, area.Top + 100, anchoredRight - area.Left + 50, height);
            Check(Bounds().Left == area.Left && Bounds().Right == anchoredRight, "actual left resize stops at the edge and anchors the right");
            Native.ResizeWithoutActivation(handle, x, area.Top + 100, width, height);
            Native.ResizeWithoutActivation(handle, x, area.Top + 100, area.Right - area.Left, height);
            Check(Bounds().Right == area.Right && Bounds().Left == x, "actual right resize stops at the edge and anchors the left");
            Native.ResizeWithoutActivation(handle, x, area.Top + 100, width, height);

            object cfg = Get(window, "cfg"), actions = Get(window, "mouseActions");
            cfg.GetType().GetProperty("AutoClick").SetValue(cfg, true);
            Call(actions, "Select", Enum.Parse(assembly.GetType("MouseAction"), "Drag"));
            var pointType = assembly.GetType("Native+POINT");
            object Point(int px, int py)
            {
                var point = Activator.CreateInstance(pointType);
                pointType.GetField("X").SetValue(point, px); pointType.GetField("Y").SetValue(point, py);
                return point;
            }
            var start = Bounds();
            Check((bool)Call(window, "BeginWindowDrag", window, Point(start.Left + 20, start.Top + 10)), "dwell move starts for boundary testing");
            ((DispatcherTimer)Get(window, "windowDragFallbackTimer")).Stop();
            cfg.GetType().GetProperty("AutoClick").SetValue(cfg, false);
            void MoveTopTo(int top) => Call(window, "UpdateWindowDrag", Point(start.Left + 20, top + 10));
            MoveTopTo(area.Top + threshold);
            Check(Bounds().Top == area.Top, "dwell movement snaps when the window edge reaches the top zone");
            MoveTopTo(area.Top + threshold + 3);
            Check(Bounds().Top == area.Top + threshold + 3, "dwell movement can leave the top snap zone again");
            MoveTopTo(area.Bottom - height - threshold);
            Check(Bounds().Bottom == area.Bottom, "dwell movement snaps at the bottom zone");
            MoveTopTo(area.Top - 200);
            Check(Bounds().Top == area.Top, "dwell movement cannot cross the top boundary");
            MoveTopTo(area.Bottom + 200);
            Check(Bounds().Bottom == area.Bottom, "dwell movement cannot cross the bottom boundary");
            Call(window, "UpdateWindowDrag", Point(area.Left - 200 + 20, start.Top + 10));
            Check(Bounds().Left == area.Left, "dwell movement cannot cross the left boundary");
            Call(window, "UpdateWindowDrag", Point(area.Right + 200 + 20, area.Bottom + 200 + 10));
            Check(Bounds().Right == area.Right && Bounds().Bottom == area.Bottom, "dwell movement respects both limits at the bottom-right corner");
            Call(window, "FinishWindowDrag");
            var persisted = disk.Load("settings.json", new Settings()).KeyboardPosition;
            Check(persisted == Position(window), "the snapped position is saved rather than the raw pointer destination");
        }
        void DockReturnPosition()
        {
            object Point(int x, int y)
            {
                var type = assembly.GetType("Native+POINT");
                object point = Activator.CreateInstance(type);
                type.GetField("X").SetValue(point, x); type.GetField("Y").SetValue(point, y);
                return point;
            }
            void Undock()
            {
                window.UpdateLayout();
                var toolbar = (Window)Get(window, "dragKeyWin");
                ((Button)Get(toolbar, "undock")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.UpdateLayout();
            }
            if ((bool)window.GetType().GetProperty("IsDragKeyDocked", Members).GetValue(window)) Undock();
            WindowPosition lastOrigin = null;
            foreach (string side in new[] { "Top", "Left", "Right" })
            {
                var toolbar = (Window)Get(window, "dragKeyWin");
                Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var keyboardBounds);
                WindowPositioning.TryWorkArea(keyboardBounds, out var area);
                WindowPositioning.Restore(new System.Windows.Interop.WindowInteropHelper(toolbar).Handle, new(area.Right - 360, area.Top + 90));
                lastOrigin = Position(toolbar);
                object cfg = Get(window, "cfg"), actions = Get(window, "mouseActions");
                cfg.GetType().GetProperty("AutoClick").SetValue(cfg, true);
                Call(actions, "Select", Enum.Parse(assembly.GetType("MouseAction"), "Drag"));
                Check((bool)Call(window, "BeginWindowDrag", toolbar, Point(lastOrigin.X + 10, lastOrigin.Y + 10)), side + " return test begins a floating DragKey move");
                ((DispatcherTimer)Get(window, "windowDragFallbackTimer")).Stop();
                cfg.GetType().GetProperty("AutoClick").SetValue(cfg, false);
                var layout = (FrameworkElement)Get(window, "dragKeyLayout");
                Point destination = layout.PointToScreen(side switch
                {
                    "Top" => new Point(layout.ActualWidth / 2, 0),
                    "Left" => new Point(0, layout.ActualHeight / 2),
                    _ => new Point(layout.ActualWidth - 60, layout.ActualHeight / 2)
                });
                Call(window, "UpdateWindowDrag", Point((int)destination.X + 10, (int)destination.Y + 10));
                Check(Position(toolbar) != lastOrigin, side + " docking target differs from the original floating position");
                Call(window, "FinishWindowDrag");
                Check(cfg.GetType().GetProperty("DragKeyPlacement").GetValue(cfg).ToString() == side, side + " drag actually docks the toolbar");
                Check(ConfigPosition("DragKeyPosition") == lastOrigin && disk.Load("settings.json", new Settings()).DragKeyPosition == lastOrigin,
                    side + " docking saves the position from before the drag rather than the drop point");
                var focus = Native.ForegroundWindow;
                Undock();
                Check(Position(toolbar) == lastOrigin, side + " undock button returns DragKey to its original position");
                Check(Native.ForegroundWindow == focus, side + " return does not take keyboard focus");
            }
            // A canceled move must not leave an old docking snapshot behind.
            var currentToolbar = (Window)Get(window, "dragKeyWin");
            object currentCfg = Get(window, "cfg"), currentActions = Get(window, "mouseActions");
            currentCfg.GetType().GetProperty("AutoClick").SetValue(currentCfg, true);
            Call(currentActions, "Select", Enum.Parse(assembly.GetType("MouseAction"), "Drag"));
            Call(window, "BeginWindowDrag", currentToolbar, Point(lastOrigin.X + 10, lastOrigin.Y + 10));
            ((DispatcherTimer)Get(window, "windowDragFallbackTimer")).Stop();
            currentCfg.GetType().GetProperty("AutoClick").SetValue(currentCfg, false);
            Call(window, "UpdateWindowDrag", Point(lastOrigin.X + 30, lastOrigin.Y + 30));
            Call(window, "CancelMouseAction");
            var canceledPosition = Position(currentToolbar);
            Call(window, "SetDragKeyPlacementAt", Enum.Parse(assembly.GetType("DragKeyPlacement"), "Top"), 0.4);
            Check(ConfigPosition("DragKeyPosition") == canceledPosition, "a canceled move cannot reuse an older return-position snapshot");
            // The bookmark also survives a real close and new instance while docked.
            Close(); window = Create();
            Check(ConfigPosition("DragKeyPosition") == canceledPosition, "docked restart preserves the return position on disk");
            Undock();
            Check(Position((Window)Get(window, "dragKeyWin")) == canceledPosition, "undocking after restart returns to the saved floating position");
            Call(window, "SetDragKeyPlacementAt", Enum.Parse(assembly.GetType("DragKeyPlacement"), "Left"), 0.2);
            var surface = (FrameworkElement)((Window)Get(window, "dragKeyWin")).GetType().GetProperty("Surface").GetValue(Get(window, "dragKeyWin"));
            Call(window, "FloatDragKeyFromBackground", surface.PointToScreen(new Point(1, 1)));
            Check(Position((Window)Get(window, "dragKeyWin")) == canceledPosition, "background/double-click undocking uses the same saved return position");
        }
        try
        {
            window = Create();
            var toolbar = (Window)Get(window, "dragKeyWin");
            ToolbarScreenEdges(toolbar);
            EdgeSnapping(toolbar);
            WindowPositioning.Restore(new System.Windows.Interop.WindowInteropHelper(window).Handle, new(80, 80));
            // A fixed screen coordinate can clamp the toolbar into a dock zone on a smaller display.
            var layout = (FrameworkElement)Get(window, "dragKeyLayout");
            Point center = layout.PointToScreen(new Point(layout.ActualWidth / 2, layout.ActualHeight / 2));
            var toolbarHandle = new System.Windows.Interop.WindowInteropHelper(toolbar).Handle;
            Check(Native.GetWindowRect(toolbarHandle, out var toolbarBounds) && WindowPositioning.Restore(toolbarHandle,
                new((int)center.X - (toolbarBounds.Right - toolbarBounds.Left) / 2,
                    (int)center.Y - (toolbarBounds.Bottom - toolbarBounds.Top) / 2)), "the floating DragKey starts in the center of the keyboard");
            var keyboardPosition = Position(window);
            var toolbarPosition = Position(toolbar);
            Check(ConfigPosition("KeyboardPosition") == keyboardPosition && ConfigPosition("DragKeyPosition") == toolbarPosition, "native moves immediately update both in-memory positions");
            Pump(550);
            var saved = disk.Load("settings.json", new Settings());
            Check(saved.KeyboardPosition == keyboardPosition && saved.DragKeyPosition == toolbarPosition, "settled positions are saved to disk while the application remains open");
            window.WindowState = WindowState.Minimized; Pump(); Close();
            saved = disk.Load("settings.json", new Settings());
            Check(saved.KeyboardPosition == keyboardPosition, "minimizing and closing never saves Windows minimized coordinates");

            window = Create(); toolbar = (Window)Get(window, "dragKeyWin");
            Check(Position(window) == keyboardPosition, "SoftKey restores its position after a real close and new instance");
            Check(Position(toolbar) == toolbarPosition, "floating DragKey restores its independent position after restart");
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Call(window, "FilterMainWindowCommands", handle, 0x0231, IntPtr.Zero, IntPtr.Zero, false);
            Native.MoveWithoutActivation(handle, keyboardPosition.X + 10, keyboardPosition.Y + 10);
            Pump(550);
            Check(disk.Load("settings.json", new Settings()).KeyboardPosition == keyboardPosition, "native move loop also defers disk writes while dragging");
            keyboardPosition = Position(window);
            Call(window, "FilterMainWindowCommands", handle, 0x0232, IntPtr.Zero, IntPtr.Zero, false);
            Check(disk.Load("settings.json", new Settings()).KeyboardPosition == keyboardPosition, "native move-loop exit immediately saves the final position");
            DwellMove(window); keyboardPosition = Position(window);
            Check(disk.Load("settings.json", new Settings()).KeyboardPosition == keyboardPosition, "SoftKey dwell movement persists when released");
            DwellMove(toolbar); toolbarPosition = Position(toolbar);
            Check(disk.Load("settings.json", new Settings()).DragKeyPosition == toolbarPosition, "DragKey dwell movement persists when released");
            Call(window, "SetDragKeyPlacementAt", Enum.Parse(assembly.GetType("DragKeyPlacement"), "Top"), 0.6);
            Check(ConfigPosition("DragKeyPosition") == toolbarPosition, "docking preserves the last floating position");
            window.WindowState = WindowState.Minimized; Pump();
            Native.MoveWithoutActivation(new System.Windows.Interop.WindowInteropHelper(toolbar).Handle, 30, 30);
            Call(window, "SaveSettings");
            Check(ConfigPosition("DragKeyPosition") == toolbarPosition, "temporary toolbar shown during minimization cannot overwrite the floating position");
            Close();

            window = Create();
            Check(Position(window) == keyboardPosition, "docked SoftKey also restores its screen position");
            saved = disk.Load("settings.json", new Settings());
            Check(saved.DragKeyPlacement == DragKeyPlacement.Top && saved.DragKeyTopPosition == 0.6 && saved.DragKeyPosition == toolbarPosition, "dock side, offset and independent floating position survive restart together");
            Check(!(bool)((Window)Get(window, "dragKeyWin")).IsVisible, "restored docked DragKey remains integrated in the keyboard");
            DockReturnPosition();
            Close(); window = null;
        }
        finally
        {
            if (window != null) { Set(window, "closeConfirmed", true); window.Close(); }
            app.Shutdown();
            foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
