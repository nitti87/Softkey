// A disconnect can be reported before the input desktop changes.
sealed class DesktopSessionState
{
    bool disconnected;
    public void Notify(int reason)
    {
        if (reason is 2 or 4 or 6) disconnected = true;
        else if (reason is 1 or 3 or 8 or 15) disconnected = false;
    }

    public bool ShouldSuspend(bool? locked, bool activeDesktop) => disconnected || locked != false || !activeDesktop;
}
