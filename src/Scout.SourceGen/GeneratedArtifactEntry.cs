namespace Scout;

internal readonly struct GeneratedArtifactEntry
{
    public GeneratedArtifactEntry(string className, string base64)
    {
        ClassName = className;
        HintName = className + ".g.cs";
        Base64 = base64;
    }

    public string ClassName { get; }

    public string HintName { get; }

    public string Base64 { get; }
}
