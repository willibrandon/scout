# Scout Libraries

Scout is a byte-oriented regex and search library stack for .NET Native AOT. The `scout` CLI is a ripgrep-compatible reference application and conformance harness for the same packages.

Install the packages from NuGet:

```sh
dotnet add package Scout.Text.Regex
dotnet add package Scout.IO.Globbing
dotnet add package Scout.IO.Ignore
```

## Scout.Text.Regex

Use `Scout.Text.Regex` when input is bytes, not necessarily valid UTF-16 text.

```csharp
using Scout.Text.Regex;

ByteRegex regex = ByteRegex.Compile(@"(?m)^Status: ([0-9]+)$");
ByteRegexMatch? match = regex.Find(data);
```

Use `ByteRegexOptions` for root regex options and engine selection:

```csharp
var options = new ByteRegexOptions
{
    MultiLine = true,
    Utf8 = false,
    EngineMode = ByteRegexEngineMode.General,
};

ByteRegex regex = ByteRegex.Compile(@"error:\s+([A-Z0-9_]+)", options);
```

`ByteRegexSet` compiles ordered multi-pattern searches:

```csharp
ByteRegexSet set = ByteRegexSet.Compile(["struct", "enum", "union"]);
ByteRegexSetMatch? match = set.Find(data);
```

## Scout.IO.Globbing

Use `Scout.IO.Globbing` for byte-oriented glob and glob-set matching.

```csharp
using Scout.IO.Globbing;

Glob glob = Glob.Parse("src/**/*.cs"u8.ToArray(), GlobOptions.UnixLiteralSeparator);
bool matched = glob.IsMatch("src/Scout.Regex/RegexMatcher.cs"u8);
```

## Scout.IO.Ignore

Use `Scout.IO.Ignore` for ripgrep-compatible recursive walking.

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

The walker understands `.ignore`, `.gitignore`, `.git/info/exclude`, global gitignore files, overrides, file types, hidden-file filtering, symbolic-link policy, and the lower-level parallel traversal model used by the `scout` CLI.
