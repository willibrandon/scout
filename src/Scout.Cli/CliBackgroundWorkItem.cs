using System.Runtime.ExceptionServices;

namespace Scout;

internal sealed class CliBackgroundWorkItem : IDisposable
{
    private readonly ManualResetEventSlim completed = new();
    private readonly Action action;
    private ExceptionDispatchInfo? failure;

    private CliBackgroundWorkItem(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        this.action = action;
    }

    internal static CliBackgroundWorkItem Queue(Action action)
    {
        var workItem = new CliBackgroundWorkItem(action);
        ThreadPool.QueueUserWorkItem(static state => ((CliBackgroundWorkItem)state!).Run(), workItem);
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
