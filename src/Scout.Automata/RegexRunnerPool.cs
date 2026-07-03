namespace Scout;

internal sealed class RegexRunnerPool<T>
    where T : class
{
    private readonly System.Collections.Concurrent.ConcurrentBag<T> items = [];
    private readonly Func<T?> factory;

    public RegexRunnerPool(T initial, Func<T?> factory)
        : this(factory)
    {
        items.Add(initial);
    }

    public RegexRunnerPool(Func<T?> factory)
    {
        this.factory = factory;
    }

    public T? Rent()
    {
        if (items.TryTake(out T? item))
        {
            return item;
        }

        return factory();
    }

    public void Return(T item)
    {
        items.Add(item);
    }
}
