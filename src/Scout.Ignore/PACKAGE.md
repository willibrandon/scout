# Scout.IO.Ignore

Scout.IO.Ignore provides ripgrep-compatible ignore handling and recursive filesystem walking for .NET applications.

```csharp
using Scout.IO.Ignore;

var options = new FileWalkerOptions
{
    Sort = FileWalkSort.FileName,
};

foreach (FileWalkEntry entry in new FileWalker(options).Enumerate("."))
{
    if (entry.IsFile)
    {
        Console.WriteLine(entry.FullPath);
    }
}
```

The walker supports `.ignore`, `.gitignore`, `.git/info/exclude`, global gitignore files, overrides, file types, hidden-file filtering, symbolic-link policy, and the lower-level parallel traversal model used by the `scout` CLI.
