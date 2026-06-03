using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Scout;

/// <summary>
/// Executes recursive directory traversal across multiple worker threads.
/// </summary>
public sealed class WalkParallel
{
    private const int MaxDefaultThreadCount = 12;
    private readonly Walk walk;
    private readonly int requestedThreads;

    internal WalkParallel(Walk walk, int requestedThreads)
    {
        ArgumentNullException.ThrowIfNull(walk);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedThreads);

        this.walk = walk;
        this.requestedThreads = requestedThreads;
    }

    /// <summary>
    /// Runs traversal with one visitor per worker.
    /// </summary>
    /// <param name="visitorFactory">Creates a visitor callback for each worker.</param>
    public void Run(Func<Func<DirEntry, WalkState>> visitorFactory)
    {
        ArgumentNullException.ThrowIfNull(visitorFactory);

        int threadCount = ResolveThreadCount();
        ConcurrentStack<WalkWorkItem>[] stacks = CreateStacks(threadCount, walk.CreateInitialWorkItems());
        if (stacks.Length == 0)
        {
            return;
        }

        int remaining = 0;
        for (int index = 0; index < stacks.Length; index++)
        {
            remaining += stacks[index].Count;
        }

        int quit = 0;
        var threads = new Thread[stacks.Length];
        ExceptionDispatchInfo? firstException = null;
        object exceptionLock = new();
        for (int index = 0; index < threads.Length; index++)
        {
            int workerIndex = index;
            threads[index] = new Thread(() =>
            {
                try
                {
                    RunWorker(workerIndex, stacks, visitorFactory(), ref remaining, ref quit);
                }
                catch (IOException exception)
                {
                    CaptureWorkerException(exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    CaptureWorkerException(exception);
                }
                catch (ArgumentException exception)
                {
                    CaptureWorkerException(exception);
                }
                catch (ObjectDisposedException exception)
                {
                    CaptureWorkerException(exception);
                }
                catch (InvalidOperationException exception)
                {
                    CaptureWorkerException(exception);
                }
                catch (NotSupportedException exception)
                {
                    CaptureWorkerException(exception);
                }
            })
            {
                IsBackground = true,
                Name = "scout-walk-worker",
            };
            threads[index].Start();
        }

        for (int index = 0; index < threads.Length; index++)
        {
            threads[index].Join();
        }

        firstException?.Throw();

        void CaptureWorkerException(Exception exception)
        {
            Volatile.Write(ref quit, 1);
            lock (exceptionLock)
            {
                firstException ??= ExceptionDispatchInfo.Capture(exception);
            }
        }
    }

    private void RunWorker(
        int workerIndex,
        ConcurrentStack<WalkWorkItem>[] stacks,
        Func<DirEntry, WalkState> visitor,
        ref int remaining,
        ref int quit)
    {
        while (Volatile.Read(ref quit) == 0)
        {
            if (!TryPopWork(workerIndex, stacks, out WalkWorkItem? item) || item is null)
            {
                if (Volatile.Read(ref remaining) == 0)
                {
                    return;
                }

                Thread.Yield();
                continue;
            }

            try
            {
                ProcessWork(workerIndex, stacks, item, visitor, ref remaining, ref quit);
            }
            finally
            {
                Interlocked.Decrement(ref remaining);
            }
        }
    }

    private void ProcessWork(
        int workerIndex,
        ConcurrentStack<WalkWorkItem>[] stacks,
        WalkWorkItem item,
        Func<DirEntry, WalkState> visitor,
        ref int remaining,
        ref int quit)
    {
        if (item.Path.TextPath == "-")
        {
            if (visitor(DirEntry.Stdin()) == WalkState.Quit)
            {
                Volatile.Write(ref quit, 1);
            }

            return;
        }

        if (!walk.TryEvaluateEntry(
            item.Path,
            item.Depth,
            item.Ancestors,
            item.IgnoreStack,
            item.RootDevice,
            item.IsRoot,
            out WalkEntryState state))
        {
            return;
        }

        WalkState visitState = WalkState.Continue;
        if (state.ShouldYield)
        {
            visitState = visitor(state.Entry);
            if (visitState == WalkState.Quit)
            {
                Volatile.Write(ref quit, 1);
                return;
            }
        }

        if (visitState == WalkState.Skip || !state.ShouldRecurse)
        {
            return;
        }

        HashSet<FileIdentity> childAncestors = item.Ancestors;
        if (!state.Entry.Identity.IsEmpty)
        {
            childAncestors = new HashSet<FileIdentity>(item.Ancestors)
            {
                state.Entry.Identity,
            };
        }

        WalkPath[] children = walk.EnumerateChildren(state.Entry);
        for (int index = children.Length - 1; index >= 0; index--)
        {
            if (Volatile.Read(ref quit) != 0)
            {
                return;
            }

            var child = new WalkWorkItem(
                children[index],
                item.Depth + 1,
                childAncestors,
                state.ChildIgnoreStack,
                item.RootDevice,
                isRoot: false);
            Interlocked.Increment(ref remaining);
            stacks[workerIndex].Push(child);
        }
    }

    private int ResolveThreadCount()
    {
        if (requestedThreads != 0)
        {
            return requestedThreads;
        }

        return Math.Min(Environment.ProcessorCount, MaxDefaultThreadCount);
    }

    private static ConcurrentStack<WalkWorkItem>[] CreateStacks(int threadCount, IEnumerable<WalkWorkItem> initialWork)
    {
        var stacks = new ConcurrentStack<WalkWorkItem>[threadCount];
        for (int index = 0; index < stacks.Length; index++)
        {
            stacks[index] = new ConcurrentStack<WalkWorkItem>();
        }

        int stackIndex = 0;
        foreach (WalkWorkItem item in initialWork)
        {
            stacks[stackIndex].Push(item);
            stackIndex = (stackIndex + 1) % stacks.Length;
        }

        return stacks;
    }

    private static bool TryPopWork(int workerIndex, ConcurrentStack<WalkWorkItem>[] stacks, out WalkWorkItem? item)
    {
        if (stacks[workerIndex].TryPop(out item))
        {
            return true;
        }

        for (int offset = 1; offset < stacks.Length; offset++)
        {
            int stealIndex = (workerIndex + offset) % stacks.Length;
            if (stacks[stealIndex].TryPop(out item))
            {
                return true;
            }
        }

        item = null;
        return false;
    }
}
