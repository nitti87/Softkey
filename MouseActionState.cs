using System;

enum MouseAction { Left, Right, Double, Drag, MiddleDrag, RightDrag }

// Only successful Windows input changes the held-button state.
sealed class MouseActionState
{
    readonly Func<int, bool> click;
    readonly Func<bool, bool> leftButton;
    readonly Func<bool, bool> middleButton;
    readonly Func<bool, bool> rightButton;

    public MouseAction Selected { get; private set; }
    public bool IsDragging { get; private set; }
    public bool IsWindowDragging { get; private set; }
    public bool IsDragSelected => IsDragAction(Selected);
    public static bool IsDragAction(MouseAction action) => action is MouseAction.Drag or MouseAction.MiddleDrag or MouseAction.RightDrag;
    public MouseAction? HeldDragButton => IsDragging && !IsWindowDragging ? Selected : null;

    public MouseActionState(Func<int, bool> click, Func<bool, bool> leftButton, Func<bool, bool> middleButton, Func<bool, bool> rightButton)
    {
        this.click = click;
        this.leftButton = leftButton;
        this.middleButton = middleButton;
        this.rightButton = rightButton;
    }

    public bool Select(MouseAction action)
    {
        if (IsDragging) return false;
        Selected = Selected == action ? MouseAction.Left : action;
        return true;
    }

    public bool Perform()
    {
        if (IsDragging) return Cancel();
        if (IsDragSelected)
        {
            if (!SetDragButton(true)) return false;
            IsDragging = true;
            return true;
        }
        int mode = Selected == MouseAction.Double ? 1 : Selected == MouseAction.Right ? 2 : 0;
        if (!click(mode)) return false;
        Selected = MouseAction.Left;
        return true;
    }

    // Moving our own windows needs no held Windows mouse button or modal DragMove loop.
    public bool BeginWindowDrag()
    {
        if (IsDragging || !IsDragSelected) return false;
        IsDragging = IsWindowDragging = true;
        return true;
    }

    public bool Cancel()
    {
        if (HeldDragButton.HasValue && !SetDragButton(false)) return false;
        IsDragging = IsWindowDragging = false;
        Selected = MouseAction.Left;
        return true;
    }

    bool SetDragButton(bool down) => Selected switch
    {
        MouseAction.MiddleDrag => middleButton(down),
        MouseAction.RightDrag => rightButton(down),
        _ => leftButton(down)
    };
}
