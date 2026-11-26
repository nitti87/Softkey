using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

enum WindowsHelloResult { Verified, NotConfigured, Cancelled, Unavailable }

static class WindowsHelloConsent
{
    const string ConsentVerifier = "Windows.Security.Credentials.UI.UserConsentVerifier";
    static readonly Guid StaticsId = new("af4f3f91-564c-4ddc-b8b5-973447627c65");
    static readonly Guid WindowInteropId = new("39e050c3-4e74-441a-8dc0-b81104df949c");
    static readonly Guid AsyncInfoId = new("00000036-0000-0000-c000-000000000046");
    // IID of IAsyncOperation<UserConsentVerificationResult> from the WinRT type signature.
    static readonly Guid VerificationOperationId = new("fd596ffd-2318-558f-9dbe-d21df43764a5");
    const int FirstInspectableMethod = 6;

    [DllImport("combase.dll")] static extern int RoInitialize(uint initType);
    [DllImport("combase.dll")] static extern void RoUninitialize();
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, in Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int WindowsCreateString(string text, uint length, out IntPtr value);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CheckAvailability(IntPtr self, out IntPtr operation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int RequestVerification(IntPtr self, IntPtr owner, IntPtr message, in Guid iid, out IntPtr operation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReadInt(IntPtr self, out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AsyncAction(IntPtr self);

    public static bool AllowsPasswordUse(this WindowsHelloResult result)
        => result is WindowsHelloResult.Verified or WindowsHelloResult.NotConfigured;

    // Call from the UI thread so the WinRT objects stay in the same apartment across awaits.
    public static async Task<WindowsHelloResult> Verify(IntPtr owner, CancellationToken cancellationToken)
    {
        bool initialized = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(RoInitialize(0)); // RO_INIT_SINGLETHREADED
            initialized = true;

            int availability = await ReadResult(BeginAvailabilityCheck(), cancellationToken);
            Trace.WriteLine($"Windows Hello availability: {availability}.");
            if (availability is 1 or 2) return WindowsHelloResult.NotConfigured; // DeviceNotPresent or NotConfiguredForUser
            if (availability != 0 || owner == IntPtr.Zero) return WindowsHelloResult.Unavailable;
            cancellationToken.ThrowIfCancellationRequested();

            var operation = BeginVerification(owner);
            Trace.WriteLine("Windows Hello verification requested.");
            int result = await ReadResult(operation, cancellationToken);
            return result switch
            {
                0 => WindowsHelloResult.Verified,
                6 => WindowsHelloResult.Cancelled,
                _ => WindowsHelloResult.Unavailable
            };
        }
        catch (OperationCanceledException) { return WindowsHelloResult.Cancelled; }
        catch (COMException error)
        {
            Trace.WriteLine($"Windows Hello verification failed (0x{error.HResult:X8}).");
            return WindowsHelloResult.Unavailable;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException or EntryPointNotFoundException)
        {
            Trace.WriteLine($"Windows Hello verification failed ({error.GetType().Name}).");
            return WindowsHelloResult.Unavailable;
        }
        finally { if (initialized) RoUninitialize(); }
    }

    static IntPtr BeginAvailabilityCheck()
    {
        var factory = GetFactory(StaticsId);
        try
        {
            Marshal.ThrowExceptionForHR(Method<CheckAvailability>(factory, FirstInspectableMethod)(factory, out var operation));
            return operation;
        }
        finally { Marshal.Release(factory); }
    }

    static IntPtr BeginVerification(IntPtr owner)
    {
        const string purpose = "Bekräfta att det är du för att använda sparade lösenord i SoftKey.";
        var factory = GetFactory(WindowInteropId);
        var message = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(purpose, (uint)purpose.Length, out message));
            Marshal.ThrowExceptionForHR(Method<RequestVerification>(factory, FirstInspectableMethod)(factory, owner, message,
                VerificationOperationId, out var operation));
            return operation;
        }
        finally
        {
            WindowsDeleteString(message);
            Marshal.Release(factory);
        }
    }

    static IntPtr GetFactory(Guid iid)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(ConsentVerifier, (uint)ConsentVerifier.Length, out var name));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, iid, out var factory));
            return factory;
        }
        finally { WindowsDeleteString(name); }
    }

    static async Task<int> ReadResult(IntPtr operation, CancellationToken cancellationToken)
    {
        var info = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(operation, in AsyncInfoId, out info));
            var readStatus = Method<ReadInt>(info, FirstInspectableMethod + 1);
            int status;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(readStatus(info, out status));
                if (status == 0) await Task.Delay(60, cancellationToken);
            } while (status == 0); // Started
            if (status == 2) throw new OperationCanceledException();
            Marshal.ThrowExceptionForHR(Method<ReadInt>(operation, FirstInspectableMethod + 2)(operation, out int result));
            return result;
        }
        finally
        {
            if (info != IntPtr.Zero)
            {
                if (cancellationToken.IsCancellationRequested) Method<AsyncAction>(info, FirstInspectableMethod + 3)(info);
                Method<AsyncAction>(info, FirstInspectableMethod + 4)(info);
                Marshal.Release(info);
            }
            Marshal.Release(operation);
        }
    }

    static T Method<T>(IntPtr instance, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
}
