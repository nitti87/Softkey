using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

// Guards WPF dispatcher loops and background tasks against transient Windows/UIA faults.
sealed class SoftKeyResilience
{
    internal const int MaxRecoverableBurst = 5;
    internal const long BurstWindowMs = 10_000;

    readonly Queue<long> recentErrors = new();
    readonly Func<long> clockMs;
    readonly Action<string> onRecover;

    internal SoftKeyResilience(Action<string> onRecover = null, Func<long> clockMs = null)
    {
        this.onRecover = onRecover;
        this.clockMs = clockMs ?? (() => Environment.TickCount64);
    }

    internal static bool IsFatal(Exception ex)
    {
        if (ex == null) return true;
        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                if (IsFatal(inner)) return true;
            return false;
        }
        if (ex is TargetInvocationException tie)
            return tie.InnerException != null && IsFatal(tie.InnerException);

        return ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException
            or ThreadAbortException;
    }

    internal bool TryRecover(Exception ex)
    {
        if (IsFatal(ex)) return false;
        long now = clockMs();
        while (recentErrors.Count > 0 && now - recentErrors.Peek() >= BurstWindowMs)
            recentErrors.Dequeue();
        if (recentErrors.Count >= MaxRecoverableBurst) return false;

        recentErrors.Enqueue(now);
        try { onRecover?.Invoke(ex.GetType().Name); }
        catch (Exception recoveryEx) when (!IsFatal(recoveryEx)) { }
        return true;
    }

    public static SoftKeyResilience Attach(Application app, string component, Action recoverState = null)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        var guard = new SoftKeyResilience(typeName =>
        {
            Trace.TraceWarning("SoftKey recovered from {0} in {1}.", typeName, component);
            LogonDiagnostics.Record($"{component}-recovered-{typeName}");
            try { recoverState?.Invoke(); }
            catch (Exception recoveryEx) when (!IsFatal(recoveryEx)) { }
        });

        app.DispatcherUnhandledException += (_, e) =>
        {
            if (guard.TryRecover(e.Exception)) e.Handled = true;
        };

        EventHandler<UnobservedTaskExceptionEventArgs> unobserved = (_, e) =>
        {
            if (IsFatal(e.Exception)) return;
            e.SetObserved();
            Trace.TraceWarning("SoftKey observed background {0} in {1}.", e.Exception.GetType().Name, component);
        };
        TaskScheduler.UnobservedTaskException += unobserved;
        app.Exit += (_, _) => TaskScheduler.UnobservedTaskException -= unobserved;
        return guard;
    }
}
