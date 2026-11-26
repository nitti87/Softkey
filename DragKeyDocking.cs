using System;
using System.Windows;

readonly record struct DragKeyDockMatch(DragKeyPlacement Placement, double Position);

readonly record struct DragKeySize(double HorizontalWidth, double TopHeight, double SideWidth, double TitleHeight,
    double ButtonHeight, Thickness PanelMargin, Thickness HorizontalButtonMargin, Thickness SideButtonMargin)
{
    public static DragKeySize Normal { get; } = new(378, 38, 80, 20, 28,
        new Thickness(3, 1, 3, 3), new Thickness(2), new Thickness(2, 1, 2, 1));
    public static DragKeySize Small { get; } = new(314, 35, 68, 17, 24,
        new Thickness(4, 3, 4, 4), new Thickness(1), new Thickness(1, 0, 1, 1));
    public static DragKeySize SignInNormal { get; } = Normal with { HorizontalWidth = 80 };
    public static DragKeySize FromSetting(bool small) => small ? Small : Normal;
    public static double SignInScale(bool small) => small ? 0.80 : 1;

    public double FloatingHeight => TitleHeight + TopHeight;

    public double SideHeight(bool rightDrag, bool middleDrag)
    {
        const int defaultDockedButtons = 9;
        int buttons = defaultDockedButtons + (rightDrag ? 1 : 0) + (middleDrag ? 1 : 0);
        int rows = (buttons + DragKeyDocking.SideColumns - 1) / DragKeyDocking.SideColumns;
        return rows * (ButtonHeight + SideButtonMargin.Top + SideButtonMargin.Bottom) + PanelMargin.Top + PanelMargin.Bottom + 2;
    }

    public void FitCell(FrameworkElement cell, bool vertical)
    {
        cell.Height = ButtonHeight;
        cell.Margin = vertical ? SideButtonMargin : HorizontalButtonMargin;
    }
}

// Geometry is in the fixed key-area coordinates. The floating toolbar's rectangle,
// not the cursor, selects an edge and a proportional position along it.
static class DragKeyDocking
{
    public const double Gap = 4, TopInset = 8;
    public const int SideColumns = 2;

    public static DragKeyDockMatch Find(Rect toolbar, Size area, DragKeyPlacement previous, double scale, DragKeySize size, double sideHeight,
        double topExtension = 0)
    {
        if (toolbar.IsEmpty || toolbar.Width <= 0 || toolbar.Height <= 0 || area.Width <= 0 || area.Height <= 0)
            return new(DragKeyPlacement.Floating, 0);
        double sideZone = Math.Min(size.SideWidth * scale + Gap, area.Width);
        var top = new Rect(0, -topExtension, area.Width, Math.Min(size.TopHeight * scale + Gap, area.Height) + topExtension);
        var left = new Rect(0, 0, sideZone, area.Height);
        var right = new Rect(area.Width - sideZone, 0, sideZone, area.Height);
        double topArea = Overlap(toolbar, top), leftArea = Overlap(toolbar, left), rightArea = Overlap(toolbar, right);
        var result = DragKeyPlacement.Floating;
        double best = 0;
        void Consider(DragKeyPlacement side, double overlap)
        {
            if (overlap <= best) return;
            result = side;
            best = overlap;
        }
        Consider(DragKeyPlacement.Top, topArea);
        Consider(DragKeyPlacement.Left, leftArea);
        Consider(DragKeyPlacement.Right, rightArea);
        double previousArea = previous switch
        {
            DragKeyPlacement.Top => topArea,
            DragKeyPlacement.Left => leftArea,
            DragKeyPlacement.Right => rightArea,
            _ => 0
        };
        // Retain a nearly tied candidate at a corner, but never retain a non-overlapping edge.
        if (previousArea > 0 && best <= previousArea * 1.1) result = previous;
        double room = AvailableTravel(result, area, scale, size, sideHeight);
        double origin = result == DragKeyPlacement.Top ? toolbar.Left - TopInset : toolbar.Top;
        return new(result, result == DragKeyPlacement.Floating || room == 0 ? 0 : Math.Clamp(origin / room, 0, 1));
    }

    public static double Offset(DragKeyPlacement side, double position, Size area, double scale, DragKeySize size, double sideHeight)
        => AvailableTravel(side, area, scale, size, sideHeight) * (double.IsFinite(position) ? Math.Clamp(position, 0, 1) : 0);

    static double AvailableTravel(DragKeyPlacement placement, Size area, double scale, DragKeySize size, double sideHeight)
        => placement == DragKeyPlacement.Top ? Math.Max(0, area.Width - 2 * TopInset - size.HorizontalWidth * scale)
            : Math.Max(0, area.Height - sideHeight * scale);

    static double Overlap(Rect first, Rect second)
        => Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left))
            * Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
}
