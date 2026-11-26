using System;
using System.Security.AccessControl;
using System.Threading;

static class DesktopStartupReady
{
    static string Name(string sid) => @"Local\SoftKey.DesktopReady.v1." + sid;

    public static EventWaitHandle Create(string sid)
    {
        try
        {
            var ready = new EventWaitHandle(false, EventResetMode.ManualReset, Name(sid), out bool created);
            if (!created) ready.Reset();
            return ready;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException) { return null; }
    }

    public static bool IsSet(string sid)
    {
        try
        {
            if (!EventWaitHandleAcl.TryOpenExisting(Name(sid), EventWaitHandleRights.Synchronize, out var ready)) return false;
            using (ready) return ready.WaitOne(0);
        }
        catch (UnauthorizedAccessException) { return false; }
    }
}
