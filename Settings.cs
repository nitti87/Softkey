using System;
using System.IO;

// Inställningar som sparas mellan starterna (settings.json i %AppData%\SoftKey).
// Ny inställning = lägg till en rad här.
class Settings
{
    public const int MinDwellMs = 200, MaxDwellMs = 5000;
    public const int MinJitterPx = 2, MaxJitterPx = 100;
    public const int MinKeyGap = 3, DefaultKeyGap = 4, MaxKeyGap = 12;
    public const int MinKeyFontSizePercent = 75, DefaultKeyFontSizePercent = 100, MaxKeyFontSizePercent = 125;

    public bool AutoClick { get; set; } = true;
    public int DwellMs { get; set; } = 800;      // tid pekaren ska vara stilla innan klick
    public int JitterPx { get; set; } = 10;      // Jitter Box: hur många pixlar pekaren får röra sig från startpunkten
    public bool Repeat { get; set; } = false;    // klicka om och om igen var DwellMs
    public int ClickMode { get; set; } = 0;      // används inte just nu (reserverad för DragKey)
    public bool AlwaysOnTop { get; set; } = true;
    public bool RightDragEnabled { get; set; }
    public bool MiddleDragEnabled { get; set; }
    public bool SmallDragKeyIcons { get; set; } // Compact toolbar; keep the saved field name for existing profiles.
    public bool SpecificSuggestionsEnabled { get; set; } = true;
    public int HorizontalKeyGap { get; set; } = DefaultKeyGap;
    public int VerticalKeyGap { get; set; } = DefaultKeyGap;
    public int KeyFontSizePercent { get; set; } = DefaultKeyFontSizePercent;
    public bool HasPersonalKeyboardLayout { get; set; }
    public bool ShareKeyboardLayoutAtSignIn { get; set; }
    public bool HideKeyboardTitleBar { get; set; }
    public bool MenuAboveDragKey { get; set; }
    public KeyboardAreaSize? KeyboardArea { get; set; }
    public WindowPosition KeyboardPosition { get; set; }
    public WindowPosition DragKeyPosition { get; set; } // Position of the floating toolbar, independent of docking.
    public DragKeyPlacement DragKeyPlacement { get; set; } = DragKeyPlacement.Top;
    public double DragKeyTopPosition { get; set; }
    public double DragKeyLeftPosition { get; set; }
    public double DragKeyRightPosition { get; set; }
    public double WindowWidth { get; set; } = 860;
    public double WindowHeight { get; set; } = 330;

    public void Normalize()
    {
        DwellMs = Math.Clamp(DwellMs, MinDwellMs, MaxDwellMs);
        JitterPx = Math.Clamp(JitterPx, MinJitterPx, MaxJitterPx);
        HorizontalKeyGap = Math.Clamp(HorizontalKeyGap, MinKeyGap, MaxKeyGap);
        VerticalKeyGap = Math.Clamp(VerticalKeyGap, MinKeyGap, MaxKeyGap);
        KeyFontSizePercent = Math.Clamp(KeyFontSizePercent, MinKeyFontSizePercent, MaxKeyFontSizePercent);
        if (KeyboardArea is { IsValid: false }) KeyboardArea = null;
        if (!Enum.IsDefined(DragKeyPlacement)) DragKeyPlacement = DragKeyPlacement.Top;
        DragKeyTopPosition = NormalizeDockPosition(DragKeyTopPosition);
        DragKeyLeftPosition = NormalizeDockPosition(DragKeyLeftPosition);
        DragKeyRightPosition = NormalizeDockPosition(DragKeyRightPosition);
        if (!double.IsFinite(WindowWidth) || WindowWidth <= 0) WindowWidth = 860;
        if (!double.IsFinite(WindowHeight) || WindowHeight <= 0) WindowHeight = 330;
    }

    static double NormalizeDockPosition(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
static class SoftKeyStorage
{
    public static readonly JsonFileStorage Default = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoftKey"));
}
