namespace Scout;

internal readonly struct FlagCatalogEntry
{
    public FlagCatalogEntry(string fullyQualifiedName)
    {
        FullyQualifiedName = fullyQualifiedName;
    }

    public string FullyQualifiedName { get; }
}
