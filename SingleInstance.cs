using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

// One input engine per Windows user and interactive session, including different app copies.
sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    public bool IsOwner { get; }
    public static readonly uint RestoreMessage = RegisterWindowMessage("SoftKey.Restore.66C34CA3-5047-48B9-A866-EFC27DE50418");

    public SingleInstance(string name = null, int waitMilliseconds = 0)
    {
        if (name == null)
        {
            using var identity = WindowsIdentity.GetCurrent();
            name = @"Local\SoftKey.66C34CA3-5047-48B9-A866-EFC27DE50418." + identity.User.Value;
        }
        mutex = new Mutex(false, name);
        try { IsOwner = mutex.WaitOne(waitMilliseconds); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }

    public static void NotifyExisting()
    {
        if (RestoreMessage != 0) PostMessage(new IntPtr(0xFFFF), RestoreMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
