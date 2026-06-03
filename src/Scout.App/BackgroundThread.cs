using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Scout;

internal sealed class BackgroundThread
{
    private readonly Thread thread;
    private ExceptionDispatchInfo? exception;

    private BackgroundThread(ThreadStart action)
    {
        ArgumentNullException.ThrowIfNull(action);

        thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (IOException caught)
            {
                exception = ExceptionDispatchInfo.Capture(caught);
            }
            catch (ObjectDisposedException caught)
            {
                exception = ExceptionDispatchInfo.Capture(caught);
            }
            catch (InvalidOperationException caught)
            {
                exception = ExceptionDispatchInfo.Capture(caught);
            }
            catch (NotSupportedException caught)
            {
                exception = ExceptionDispatchInfo.Capture(caught);
            }
            catch (ArgumentException caught)
            {
                exception = ExceptionDispatchInfo.Capture(caught);
            }
        })
        {
            IsBackground = true,
            Name = "scout-background-worker",
        };
    }

    internal static BackgroundThread Start(ThreadStart action)
    {
        var worker = new BackgroundThread(action);
        worker.thread.Start();
        return worker;
    }

    internal void Join()
    {
        thread.Join();
        exception?.Throw();
    }
}
