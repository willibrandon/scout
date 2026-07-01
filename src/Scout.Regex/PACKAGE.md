# Scout.Text.Regex

Scout.Text.Regex provides byte-oriented regular expressions for .NET applications that need predictable search behavior over UTF-8, mixed, or arbitrary byte data.

```csharp
using Scout.Text.Regex;

ByteRegex regex = ByteRegex.Compile(@"(?m)^Status: ([0-9]+)$");
ByteRegexCaptures? captures = regex.FindCaptures(data);
```

The engine is backed by Scout's automata implementation and is designed for Native AOT, trimming, and linear-time search.
