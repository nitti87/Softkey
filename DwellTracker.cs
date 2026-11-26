using System;

enum DwellTarget { None, AutoClick, RightClick, DoubleClick, Drag, DragDrop, MoveKeyboard, MoveDragKey, UndockDragKey, ResizeLeft, ResizeRight, ResizeTop, ResizeBottom, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight, SoftKeyControl, SoftKeyRepeat, External, ToggleKeyboard, ToggleKeyboardHeader, MiddleDrag, RightDrag, CloseSoftKey, MoveKeyboardEdge }

// Tracks one dwell visit. A change of control also starts a new dwell, even inside the jitter box.
sealed class DwellTracker
{
    int anchorX, anchorY;
    double since;
    DwellTarget target;
    object control;
    (int X, int Y) lastPoint;
    (int X, int Y)? blockedPoint;
    bool initialized, fired, repeatSuppressed, adoptControl;

    public int VisitVersion { get; private set; }

    public bool IsBlockedAt(int x, int y) => blockedPoint == (x, y);

    public void Reset()
    {
        // A redraw or transient hit-test failure is not pointer movement.
        initialized = false;
        control = null;
        adoptControl = false;
        VisitVersion++;
    }

    public bool Update(int x, int y, DwellTarget nextTarget, double now, int dwellMs, int jitterPx, bool repeat,
        object nextControl = null)
    {
        var point = (x, y);
        if (blockedPoint != point) blockedPoint = null;
        if (nextTarget == DwellTarget.None) { Reset(); return false; }
        if (adoptControl) { control = nextControl; adoptControl = false; }
        if (blockedPoint.HasValue) return false;
        lastPoint = point;
        if (!initialized || target != nextTarget || !Equals(control, nextControl)
            || Math.Abs((long)x - anchorX) > jitterPx || Math.Abs((long)y - anchorY) > jitterPx)
        {
            anchorX = x;
            anchorY = y;
            target = nextTarget;
            control = nextControl;
            since = now;
            initialized = true;
            fired = repeatSuppressed = false;
            return false;
        }
        return !(fired && (!repeat || repeatSuppressed)) && now - since >= dwellMs;
    }

    public void Complete(double now, bool allowRepeat)
    {
        fired = true;
        repeatSuppressed = !allowRepeat;
        blockedPoint = initialized && repeatSuppressed ? lastPoint : null;
        since = now;
    }

    public double Progress(double now, int dwellMs)
    {
        if (!initialized) return 0;
        if (fired && repeatSuppressed) return 1;
        return Math.Clamp((now - since) / dwellMs, 0, 1);
    }

    public void Suppress(int x, int y, DwellTarget nextTarget, double now)
    {
        Reset();
        blockedPoint = null;
        Update(x, y, nextTarget, now, 0, 0, false);
        Complete(now, allowRepeat: false);
        blockedPoint = (x, y);
        // The first hit test after a drag/drop or mode change belongs to this suppressed visit.
        adoptControl = true;
    }
}
