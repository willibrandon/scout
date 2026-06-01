namespace Scout;

internal readonly struct FlagCatalogEntry
{
    public FlagCatalogEntry(string typeName, string fullyQualifiedName, int order)
    {
        TypeName = typeName;
        FullyQualifiedName = fullyQualifiedName;
        Order = order;
    }

    public string TypeName { get; }

    public string FullyQualifiedName { get; }

    public int Order { get; }
}
