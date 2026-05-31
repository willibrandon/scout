namespace Scout;

internal readonly struct FlagCatalogEntry
{
    public FlagCatalogEntry(string typeName, string fullyQualifiedName)
    {
        TypeName = typeName;
        FullyQualifiedName = fullyQualifiedName;
    }

    public string TypeName { get; }

    public string FullyQualifiedName { get; }
}
