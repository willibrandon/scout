# Scout.IO.Globbing

Scout.IO.Globbing provides byte-oriented glob and glob-set matching for .NET applications.

```csharp
using Scout.IO.Globbing;

Glob glob = Glob.Parse("src/**/*.cs"u8.ToArray(), GlobOptions.UnixLiteralSeparator);
bool matched = glob.IsMatch("src/Scout.Regex/RegexMatcher.cs"u8);
```

The implementation is used by Scout's ripgrep-compatible search stack and is designed for Native AOT and trimming.
