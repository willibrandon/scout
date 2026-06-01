using Microsoft.CodeAnalysis;

namespace Scout;

internal readonly struct FlagCatalogEntry
{
    public FlagCatalogEntry(string typeName, string fullyQualifiedName, int order, Location? location)
    {
        TypeName = typeName;
        FullyQualifiedName = fullyQualifiedName;
        Order = order;
        Location = location;
    }

    public string TypeName { get; }

    public string FullyQualifiedName { get; }

    public int Order { get; }

    public Location? Location { get; }
}
