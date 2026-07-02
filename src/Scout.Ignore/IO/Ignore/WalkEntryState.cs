namespace Scout.IO.Ignore;

internal readonly struct WalkEntryState
{
    public WalkEntryState(
        DirEntry entry,
        IgnoreStack childIgnoreStack,
        bool shouldYield,
        bool shouldRecurse)
    {
        Entry = entry;
        ChildIgnoreStack = childIgnoreStack;
        ShouldYield = shouldYield;
        ShouldRecurse = shouldRecurse;
    }

    public DirEntry Entry { get; }

    public IgnoreStack ChildIgnoreStack { get; }

    public bool ShouldYield { get; }

    public bool ShouldRecurse { get; }
}
