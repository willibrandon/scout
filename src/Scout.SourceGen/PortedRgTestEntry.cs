namespace Scout;

internal readonly struct PortedRgTestEntry
{
    public PortedRgTestEntry(string sourceFile, string name)
    {
        SourceFile = sourceFile;
        Name = name;
    }

    public string SourceFile { get; }

    public string Name { get; }
}
