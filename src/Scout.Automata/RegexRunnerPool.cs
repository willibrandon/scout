namespace Scout;

internal sealed class RegexRunnerPool<T>
    where T : class
{
    private const int LocalSlotCount = 16;

    private readonly RegexRunnerPoolSlot<T>[] localSlots = CreateLocalSlots();
    private readonly System.Collections.Concurrent.ConcurrentBag<T> overflow = [];
    private readonly Func<T?> factory;

    public RegexRunnerPool(T initial, Func<T?> factory)
        : this(factory)
    {
        overflow.Add(initial);
    }

    public RegexRunnerPool(Func<T?> factory)
    {
        this.factory = factory;
    }

    public T? Rent()
    {
        int threadId = Environment.CurrentManagedThreadId;

        for (int i = 0; i < localSlots.Length; i++)
        {
            RegexRunnerPoolSlot<T> slot = localSlots[i];
            if (System.Threading.Volatile.Read(ref slot.ThreadId) != threadId)
            {
                continue;
            }

            T? localItem = System.Threading.Interlocked.Exchange(ref slot.Item, null);
            if (localItem is not null)
            {
                return localItem;
            }

            break;
        }

        if (overflow.TryTake(out T? item))
        {
            return item;
        }

        return factory();
    }

    public void Return(T item)
    {
        int threadId = Environment.CurrentManagedThreadId;
        RegexRunnerPoolSlot<T>? emptySlot = null;

        for (int i = 0; i < localSlots.Length; i++)
        {
            RegexRunnerPoolSlot<T> slot = localSlots[i];
            int owner = System.Threading.Volatile.Read(ref slot.ThreadId);

            if (owner == threadId)
            {
                if (System.Threading.Interlocked.CompareExchange(ref slot.Item, item, null) is null)
                {
                    return;
                }

                overflow.Add(item);
                return;
            }

            if (owner == 0 && emptySlot is null)
            {
                emptySlot = slot;
            }
        }

        if (emptySlot is not null &&
            System.Threading.Interlocked.CompareExchange(ref emptySlot.ThreadId, threadId, 0) == 0)
        {
            System.Threading.Volatile.Write(ref emptySlot.Item, item);
            return;
        }

        overflow.Add(item);
    }

    private static RegexRunnerPoolSlot<T>[] CreateLocalSlots()
    {
        var slots = new RegexRunnerPoolSlot<T>[LocalSlotCount];

        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new RegexRunnerPoolSlot<T>();
        }

        return slots;
    }
}
