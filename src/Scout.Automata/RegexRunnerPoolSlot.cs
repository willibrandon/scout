namespace Scout;

internal sealed class RegexRunnerPoolSlot<T>
    where T : class
{
    internal int ThreadId;
    internal T? Item;
}
