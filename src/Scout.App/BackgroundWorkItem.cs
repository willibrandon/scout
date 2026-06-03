using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Scout;

internal sealed class BackgroundWorkItem : IDisposable
{
    private readonly ManualResetEventSlim completed = new();
    private readonly Action action;
    private ExceptionDispatchInfo? failure;

    private BackgroundWorkItem(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        this.action = action;
    }

    internal static BackgroundWorkItem Queue(Action action)
    {
        var workItem = new BackgroundWorkItem(action);
        ThreadPool.QueueUserWorkItem(static state => ((BackgroundWorkItem)state!).Run(), workItem);
        return workItem;
    }

    internal void Join()
    {
        completed.Wait();
        failure?.Throw();
    }

    public void Dispose()
    {
        completed.Wait();
        completed.Dispose();
    }

    private void Run()
    {
        try
        {
            action();
        }
        catch (Exception exception) when (CaptureFailure(exception))
        {
        }
        finally
        {
            completed.Set();
        }
    }

    private bool CaptureFailure(Exception exception)
    {
        failure = ExceptionDispatchInfo.Capture(exception);
        return true;
    }
}
