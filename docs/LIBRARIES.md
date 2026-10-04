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

Set `MatchInvalidUtf8 = true` when Unicode scalar operations should treat each malformed or truncated UTF-8 byte as one `U+FFFD`, following Go's byte-regexp behavior. The default remains ripgrep-compatible. Matches and captures always retain offsets into the original bytes; valid encoded `U+FFFD` remains three bytes, and raw byte mode is unchanged.

`ByteRegexSet` compiles ordered multi-pattern searches:

```csharp
ByteRegexSet set = ByteRegexSet.Compile(["struct", "enum", "union"]);
ByteRegexSetMatch? match = set.Find(data);
```

Compiled `ByteRegex`, `ByteRegexSet`, `RegexAutomaton`, and `PatternSet` instances are safe to share across threads for matching. Callback state passed to iteration APIs remains caller-owned.

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

`WalkBuilder.FromPaths(paths)` copies and validates a collection of roots once;
`Walk.FromPaths(paths)` creates its serial walk directly. Empty collections and
`new WalkBuilder()` yield no entries. Each root keeps its own ignore context.

```csharp
Walk walk = WalkBuilder.FromPaths(["src", "tests"])
    .ErrorHandler(error =>
    {
        Console.Error.WriteLine($"{error.Path}: {error.Message}");
        return WalkState.Continue;
    })
    .Build();
```

Filesystem failures throw `WalkException` by default. Its `Path` preserves raw
Unix bytes, `Depth` is relative to the traversal root, and `InnerException`
retains the filesystem cause. An optional `ErrorHandler` on `WalkBuilder` or
`FileWalkerOptions` receives the same exception: `Continue` and `Skip` skip the
failed operation, and `Quit` ends the walk. Lazy metadata failures follow this
policy as well. Parallel error handlers can run concurrently and must protect
their own shared state.
