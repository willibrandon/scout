namespace Scout;

internal sealed class DirectoryFixture : IDisposable
{
    internal DirectoryFixture()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scout-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string Write(string path, string contents = "match\n")
    {
        ArgumentNullException.ThrowIfNull(path);
        string fullPath = System.IO.Path.Combine(Root, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
