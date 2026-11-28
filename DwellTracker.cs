using System;
using System.Collections.Generic;

enum DwellTarget { None, AutoClick, RightClick, DoubleClick, Drag, DragDrop, MoveKeyboard, MoveDragKey, UndockDragKey, ResizeLeft, ResizeRight, ResizeTop, ResizeBottom, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight, SoftKeyControl, SoftKeyRepeat, External, ToggleKeyboard, ToggleKeyboardHeader, MiddleDrag, RightDrag, CloseSoftKey, MoveKeyboardEdge }

// Tracks one dwell visit. A change of control also starts a new dwell, even inside the jitter box.
sealed class DwellTracker
{
    const int RearmDistancePx = 4;
    const int SameTargetRearmDistancePx = 12;
    const int RearmDelayMs = 120;
    const int MaxRearmSampleGapMs = 200;
    const int RecentTravelWindowMs = 200;
    const int RecentTravelTolerancePx = 6;
    int anchorX, anchorY;
    double since;
    DwellTarget target;
    object control;
    (int X, int Y) lastPoint;
    (int X, int Y)? lastMotionPoint;
    readonly Queue<(int X, int Y, double Time)> recentMotion = new();
    AxisTravel horizontalTravel, verticalTravel;
    (int X, int Y)? clickPoint;
    DwellTarget clickedTarget;
    object clickedControl;
    double? outsideClickAreaSince;
    double? outsideSameTargetAreaSince;
    double lastOutsideSample;
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
    public bool ObserveMovement(int x, int y, int jitterPx, double now)
    {
        bool moving = ObserveTravel(x, y, now);
        if (clickPoint is { } origin) ObserveClickDeparture(x, y, origin, jitterPx, now);
        // The aiming area can be wide; a click still needs a stationary visit within it.
        if (initialized && (moving || MovedBeyond(x, y, anchorX, anchorY, jitterPx))) Reset();
        return repeating || !clickPoint.HasValue || outsideClickAreaSince.HasValue;
    }

    bool ObserveTravel(int x, int y, double now)
    {
        var previous = lastMotionPoint;
        lastMotionPoint = (x, y);
        if (previous is not { } point)
        {
            recentMotion.Enqueue((x, y, now));
            return false;
        }
        if (point.X == x && point.Y == y) return false;

        while (recentMotion.TryPeek(out var oldest) && now - oldest.Time > RecentTravelWindowMs) recentMotion.Dequeue();
        bool travelled = MovedBeyond(x, y, point.X, point.Y, RecentTravelTolerancePx);
        foreach (var sample in recentMotion)
        {
            if (!MovedBeyond(x, y, sample.X, sample.Y, RecentTravelTolerancePx)) continue;
            travelled = true;
            break;
        }
        recentMotion.Enqueue((x, y, now));

        // Slow movement in one direction can be meaningful before it leaves the recent movement area.
        bool horizontal = horizontalTravel.Observe((long)x - point.X, now);
        bool vertical = verticalTravel.Observe((long)y - point.Y, now);
        return travelled || horizontal || vertical;
    }

    struct AxisTravel
    {
        const int MinSamples = 3;
        const int MinDistancePx = 4;
        const int MaxMotionGapMs = 180;
        int direction, samples;
        long distance;
        double lastMove;

        public bool Observe(long delta, double now)
        {
            if (delta == 0) return false;
            int nextDirection = Math.Sign(delta);
            if (direction != nextDirection || now - lastMove > MaxMotionGapMs)
            {
                direction = nextDirection;
                samples = 0;
                distance = 0;
            }
            lastMove = now;
            samples = Math.Min(samples + 1, MinSamples);
            distance = Math.Min(distance + Math.Abs(delta), MinDistancePx);
            return samples >= MinSamples && distance >= MinDistancePx;
        }
    }

    void ObserveClickDeparture(int x, int y, (int X, int Y) origin, int jitterPx, double now)
    {
        bool outside = MovedBeyond(x, y, origin.X, origin.Y, RearmDistancePx - 1);
        if (!outside)
        {
            if (outsideClickAreaSince.HasValue && !repeating) Reset();
            outsideClickAreaSince = null;
            outsideSameTargetAreaSince = null;
            return;
        }

        if (!outsideClickAreaSince.HasValue || now - lastOutsideSample > MaxRearmSampleGapMs)
        {
            if (!repeating) Reset();
            outsideClickAreaSince = now;
            outsideSameTargetAreaSince = null;
        }
        lastOutsideSample = now;

        int sameTargetDistance = clickedTarget == DwellTarget.None ? RearmDistancePx
            : Math.Clamp(jitterPx, RearmDistancePx, SameTargetRearmDistancePx);
        if (!MovedBeyond(x, y, origin.X, origin.Y, sameTargetDistance - 1))
        {
            if (outsideSameTargetAreaSince.HasValue && initialized && !repeating && !IsDistinctClickTarget(target, control)) Reset();
            outsideSameTargetAreaSince = null;
            return;
        }

        outsideSameTargetAreaSince ??= now;
        if (now - outsideSameTargetAreaSince.Value < RearmDelayMs) return;
        clickPoint = null;
        clickedControl = null;
        outsideClickAreaSince = null;
        outsideSameTargetAreaSince = null;
    }

    bool IsDistinctClickTarget(DwellTarget nextTarget, object nextControl)
    {
        if (clickedTarget == DwellTarget.None) return false;
        if (clickedControl is ValueTuple<IntPtr, string> previous && nextControl is ValueTuple<IntPtr, string> next)
        {
            // A provider falling back to its window does not identify a different control in that window.
            return previous.Item1 != next.Item1 || (previous.Item2 != null && next.Item2 != null && previous.Item2 != next.Item2);
        }
        if (clickedControl != null && nextControl != null) return !Equals(clickedControl, nextControl);
        return clickedTarget != nextTarget;
    }

    public bool Update(int x, int y, DwellTarget nextTarget, double now, int dwellMs, int jitterPx, bool repeat,
        object nextControl = null)
    {
        if (!ObserveMovement(x, y, jitterPx, now)) return false;
        if (nextTarget == DwellTarget.None) { Reset(); return false; }
        bool sameVisit = initialized && target == nextTarget && Equals(control, nextControl);
        bool differentTarget = outsideClickAreaSince.HasValue && IsDistinctClickTarget(nextTarget, nextControl);
        if (clickPoint.HasValue && !(repeating && sameVisit) && !differentTarget && !outsideSameTargetAreaSince.HasValue)
        {
            if (initialized) Reset();
            return false;
        }
        if (!sameVisit)
        {
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
        // The current target can accumulate dwell time while the pointer's departure is being confirmed.
        bool rearmed = !clickPoint.HasValue || (differentTarget && now - outsideClickAreaSince.Value >= RearmDelayMs);
        return (repeating ? repeat : rearmed) && now - since >= dwellMs;
    }

    public void Complete(double now, bool allowRepeat)
    {
        if (initialized)
        {
            clickPoint = lastPoint;
            clickedTarget = target;
            clickedControl = control;
        }
        outsideClickAreaSince = null;
        outsideSameTargetAreaSince = null;
        repeating = initialized && allowRepeat;
        since = now;
    }

    public double Progress(double now, int dwellMs)
    {
        if (!initialized) return 0;
        if (clickPoint.HasValue && !repeating && !outsideClickAreaSince.HasValue) return 1;
        return Math.Clamp((now - since) / dwellMs, 0, 1);
    }

    public void Suppress(int x, int y)
    {
        Reset();
        clickPoint = (x, y);
        clickedTarget = DwellTarget.None;
        clickedControl = null;
        outsideClickAreaSince = null;
        outsideSameTargetAreaSince = null;
    }
}
