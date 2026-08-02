# Scout.Text.Regex

Scout.Text.Regex provides byte-oriented regular expressions for .NET applications that need predictable search behavior over UTF-8, mixed, or arbitrary byte data.

```csharp
using Scout.Text.Regex;

ByteRegex regex = ByteRegex.Compile(@"(?m)^Status: ([0-9]+)$");
ByteRegexCaptures? captures = regex.FindCaptures(data);
```

The engine is backed by Scout's automata implementation and is designed for Native AOT, trimming, and linear-time search.

Compiled `ByteRegex` and `ByteRegexSet` instances are safe to share across threads for matching. Callback state passed to iteration APIs remains caller-owned.

Unanchored searches use leftmost-first semantics: the earliest possible start is selected before greedy or lazy repetition determines the preferred match at that start. Match and capture offsets remain consistent across PikeVM and DFA execution.

Malformed UTF-8 does not participate in Unicode scalar matches by default, matching ripgrep's behavior. Callers that process Go-style byte text can opt into one replacement scalar per malformed byte:

```csharp
var options = new ByteRegexOptions { MatchInvalidUtf8 = true };
ByteRegex replacement = ByteRegex.Compile(@"\u{FFFD}+", options);
ByteRegexMatch? match = replacement.Find(data);
```

Every malformed or truncated byte is treated as `U+FFFD` with a width of one byte. A valid UTF-8 encoding of `U+FFFD` keeps its three-byte width, and all matches, captures, and values continue to reference the original input bytes. The option has no effect on raw byte operations when both `Utf8` and `UnicodeClasses` are disabled.
