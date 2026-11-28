using System;

enum DwellTarget { None, AutoClick, RightClick, DoubleClick, Drag, DragDrop, MoveKeyboard, MoveDragKey, UndockDragKey, ResizeLeft, ResizeRight, ResizeTop, ResizeBottom, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight, SoftKeyControl, SoftKeyRepeat, External, ToggleKeyboard, ToggleKeyboardHeader, MiddleDrag, RightDrag, CloseSoftKey, MoveKeyboardEdge }

// Tracks one dwell visit. A change of control also starts a new dwell, even inside the jitter box.
sealed class DwellTracker
{
    const int RearmDistancePx = 4;
    int anchorX, anchorY;
    double since;
    DwellTarget target;
    object control;
    (int X, int Y) lastPoint;
    (int X, int Y)? clickPoint;
    bool initialized, repeating;

    public int VisitVersion { get; private set; }

    public void Reset()
    {
        // A redraw or transient hit-test failure is not pointer movement.
        initialized = false;
        control = null;
        repeating = false;
        VisitVersion++;
    }

    static bool MovedBeyond(int x, int y, int fromX, int fromY, int tolerance)
        => Math.Abs((long)x - fromX) > tolerance || Math.Abs((long)y - fromY) > tolerance;

    // Sample movement even when control identification is still running on a worker.
    public bool ObserveMovement(int x, int y, int jitterPx)
    {
        if (clickPoint is { } origin && MovedBeyond(x, y, origin.X, origin.Y, RearmDistancePx - 1))
        {
            clickPoint = null;
            if (!repeating) Reset();
        }
        if (initialized && MovedBeyond(x, y, anchorX, anchorY, jitterPx)) Reset();
        return repeating || !clickPoint.HasValue;
    }

    public bool Update(int x, int y, DwellTarget nextTarget, double now, int dwellMs, int jitterPx, bool repeat,
        object nextControl = null)
    {
        if (!ObserveMovement(x, y, jitterPx)) return false;
        if (nextTarget == DwellTarget.None) { Reset(); return false; }
        if (!initialized || target != nextTarget || !Equals(control, nextControl))
        {
            // An enabled repeat belongs to the control that was clicked.
            if (clickPoint.HasValue) { Reset(); return false; }
            anchorX = x;
            anchorY = y;
            target = nextTarget;
            control = nextControl;
            since = now;
            initialized = true;
            repeating = false;
            lastPoint = (x, y);
            return false;
        }
        lastPoint = (x, y);
        return (!repeating || repeat) && now - since >= dwellMs;
    }

    public void Complete(double now, bool allowRepeat)
    {
        if (initialized) clickPoint = lastPoint;
        repeating = initialized && allowRepeat;
        since = now;
    }

    public double Progress(double now, int dwellMs)
    {
        if (!initialized) return 0;
        if (clickPoint.HasValue && !repeating) return 1;
        return Math.Clamp((now - since) / dwellMs, 0, 1);
    }

    public void Suppress(int x, int y)
    {
        Reset();
        clickPoint = (x, y);
    }
}
