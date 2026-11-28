using System;
using System.Linq;
using System.Windows.Media;

// Beskriver tangentbordets geometri och tangenter utan att bygga WPF-kontroller.
// Ändra rader, kolumnvikter eller tangentvikter här när layouten ska justeras.
static class KeyboardLayout
{
    public const double SuggestionColumnWeight = 104;
    public const double KeyboardColumnWeight = 688;
    public const double NavigationColumnWeight = 60;
    public const double NavigationGapWeight = 32;
    public const double KeyAreaWeight = KeyboardColumnWeight + NavigationGapWeight + NavigationColumnWeight;
    public const double KeyUnitsPerRow = 16.5;
    public const double TotalColumnWeight = SuggestionColumnWeight + KeyAreaWeight;
    public static double LegendFontSize(double rowHeight, double keyUnitWidth)
        => Math.Min(rowHeight * 0.44, keyUnitWidth * 0.41);

    public static int ScaledGap(int gap, int fontSizePercent)
        => Math.Clamp((int)Math.Round(gap * (double)fontSizePercent / Settings.DefaultKeyFontSizePercent,
            MidpointRounding.AwayFromZero), Settings.MinKeyGap, Settings.MaxKeyGap);

    public static bool IsPrintable(KeyDef key)
    {
        if (key.Ext) return false;
        ushort scan = key.Scan;
        return (scan >= 0x02 && scan <= 0x0D)
            || (scan >= 0x10 && scan <= 0x1B)
            || (scan >= 0x1E && scan <= 0x2B)
            || (scan >= 0x2C && scan <= 0x35)
            || scan == 0x56;
    }

    static KeyDef K(string label, ushort scan, double width = 1, bool extended = false, char character = '\0', bool modifier = false, Brush foreground = null, ushort virtualKey = 0)
        => new(label, scan, width, extended, character, modifier, foreground, virtualKey);

    static KeyDef[] Letters(string chars, int start)
        => chars.Select((c, i) => K(c.ToString(), (ushort)(start + i), character: c)).ToArray();

    public static readonly KeyDef[][] Rows =
    {
        new ushort[] { 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58 }
            .Select((scan, i) => K("F" + (i + 1), scan, 1.375)).ToArray(),
        new[] { K("Esc", 0x01), K("§", 0x29) }
            .Concat("1234567890".Select((c, i) => K(c.ToString(), (ushort)(2 + i))))
            .Concat(new[] { K("+", 0x0C), K("´", 0x0D), K("BkSp", 0x0E, 1.5), K("Pse", 0, virtualKey: 0x13) })
            .ToArray(),
        new[] { K("Tab", 0x0F, 1.5) }
            .Concat(Letters("qwertyuiopå", 0x10))
            .Concat(new[] { K("¨", 0x1B), K("NL", 0x45, 1.5, extended: true), K("SL", 0x46, 1.5) })
            .ToArray(),
        new[] { K("Cap", 0x3A, 1.5) }
            .Concat(Letters("asdfghjklöä", 0x1E))
            .Concat(new[] { K("'", 0x2B), K("Enter", 0x1C, 3) })
            .ToArray(),
        new[] { K("Sft", 0x2A, 1.5, modifier: true), K("<", 0x56) }
            .Concat(Letters("zxcvbnm", 0x2C))
            .Concat(new[] { K(",", 0x33), K(".", 0x34), K("-", 0x35), K("Sft", 0x36, 2, modifier: true), K("PrtScr", 0x37, 2, extended: true) })
            .ToArray(),
        new[]
        {
            K("Ctl", 0x1D, 1.3, modifier: true), K("Win", 0x5B, 1, extended: true, modifier: true),
            K("Alt", 0x38, 1.2, modifier: true), K("Space", 0x39, 4.35),
            K("AltGr", 0x38, 1.35, extended: true, modifier: true), K("Win", 0x5C, 1, extended: true, modifier: true),
            K("Menu", 0x5D, 1, extended: true), K("Ctl", 0x1D, 1.3, extended: true, modifier: true),
            K("←", 0x4B, extended: true), K("→", 0x4D, extended: true), K("↑", 0x48, extended: true), K("↓", 0x50, extended: true)
        }
    };

    public static readonly KeyDef[] NavigationKeys =
    {
        K("Ins", 0x52, 1.2, extended: true), K("Del", 0x53, 1.2, extended: true), K("Hme", 0x47, 1.2, extended: true),
        K("End", 0x4F, 1.2, extended: true), K("PgU", 0x49, 1.2, extended: true), K("PgD", 0x51, 1.2, extended: true)
    };
}
