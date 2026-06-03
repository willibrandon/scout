
namespace Scout;

/// <summary>
/// Represents a byte-oriented glob pattern.
/// </summary>
public sealed class Glob
{
    private readonly byte[] pattern;
    private readonly GlobOptions options;

    private Glob(byte[] pattern, GlobOptions options)
    {
        this.pattern = pattern;
        this.options = options;
    }

    /// <summary>
    /// Gets the original pattern bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Pattern => pattern;

    /// <summary>
    /// Gets the options used by this glob.
    /// </summary>
    public GlobOptions Options => options;

    /// <summary>
    /// Parses a glob with Unix-style default options.
    /// </summary>
    /// <param name="pattern">The glob pattern bytes.</param>
    /// <returns>A parsed glob.</returns>
    public static Glob Parse(byte[] pattern)
    {
        return Parse(pattern, GlobOptions.Unix);
    }

    /// <summary>
    /// Parses a glob.
    /// </summary>
    /// <param name="pattern">The glob pattern bytes.</param>
    /// <param name="options">The glob options.</param>
    /// <returns>A parsed glob.</returns>
    public static Glob Parse(byte[] pattern, GlobOptions options)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(options);

        ValidatePattern(pattern, options);
        return new Glob(pattern.AsSpan().ToArray(), options);
    }

    /// <summary>
    /// Creates a builder for configuring a glob pattern.
    /// </summary>
    /// <param name="pattern">The glob pattern bytes.</param>
    /// <returns>A glob builder.</returns>
    public static GlobBuilder Builder(byte[] pattern)
    {
        return new GlobBuilder(pattern);
    }

    /// <summary>
    /// Escapes glob metacharacters by wrapping them in character classes.
    /// </summary>
    /// <param name="pattern">The bytes to escape.</param>
    /// <returns>The escaped glob pattern bytes.</returns>
    public static byte[] Escape(ReadOnlySpan<byte> pattern)
    {
        var escaped = new List<byte>(pattern.Length);
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value is (byte)'?' or (byte)'*' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}')
            {
                escaped.Add((byte)'[');
                escaped.Add(value);
                escaped.Add((byte)']');
                continue;
            }

            escaped.Add(value);
        }

        return escaped.ToArray();
    }

    /// <summary>
    /// Tests whether this glob matches a path.
    /// </summary>
    /// <param name="path">The path bytes.</param>
    /// <returns><see langword="true" /> when the glob matches the path.</returns>
    public bool IsMatch(ReadOnlySpan<byte> path)
    {
        return IsMatch(path, GetBaseName(path));
    }

    /// <summary>
    /// Tests whether this glob matches a prepared candidate path.
    /// </summary>
    /// <param name="candidate">The prepared candidate path.</param>
    /// <returns><see langword="true" /> when the glob matches the candidate.</returns>
    public bool IsMatch(GlobCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return IsMatch(candidate.Path.Span, candidate.BaseName.Span);
    }

    private bool IsMatch(ReadOnlySpan<byte> path, ReadOnlySpan<byte> baseName)
    {
        if (Match(pattern, patternIndex: 0, path, pathIndex: 0))
        {
            return true;
        }

        return options.MatchBaseName
            && !PatternContainsSeparator()
            && Match(pattern, patternIndex: 0, baseName, pathIndex: 0);
    }

    internal byte[] ToRegexCandidatePattern()
    {
        var candidate = new List<byte>();
        if (options.AsciiCaseInsensitive)
        {
            AppendAscii(candidate, "(?i)"u8);
        }

        int patternIndex = 0;
        while (patternIndex < pattern.Length)
        {
            if (IsEscapedLiteral(pattern, patternIndex))
            {
                AppendHexLiteral(candidate, pattern[patternIndex + 1]);
                patternIndex += 2;
                continue;
            }

            byte token = pattern[patternIndex];
            if (token == (byte)'*')
            {
                while (patternIndex < pattern.Length && pattern[patternIndex] == (byte)'*')
                {
                    patternIndex++;
                }

                AppendAnyBytes(candidate);
                continue;
            }

            if (token == (byte)'?')
            {
                patternIndex++;
                AppendAnyBytes(candidate);
                continue;
            }

            if (token == (byte)'[')
            {
                int classEnd = FindClassEnd(pattern, patternIndex);
                if (classEnd > patternIndex)
                {
                    patternIndex = classEnd + 1;
                    AppendAnyBytes(candidate);
                    continue;
                }
            }

            if (token == (byte)'{')
            {
                int braceEnd = FindBraceEnd(pattern, patternIndex);
                if (braceEnd > patternIndex)
                {
                    patternIndex = braceEnd + 1;
                    AppendAnyBytes(candidate);
                    continue;
                }
            }

            AppendHexLiteral(candidate, token);
            patternIndex++;
        }

        return candidate.ToArray();
    }

    internal bool TryGetLiteral(out byte[] literal)
    {
        var bytes = new List<byte>();
        if (options.AsciiCaseInsensitive)
        {
            literal = [];
            return false;
        }

        int patternIndex = 0;
        while (patternIndex < pattern.Length)
        {
            if (IsEscapedLiteral(pattern, patternIndex))
            {
                bytes.Add(pattern[patternIndex + 1]);
                patternIndex += 2;
                continue;
            }

            byte token = pattern[patternIndex];
            if (IsGlobMetacharacter(token))
            {
                literal = [];
                return false;
            }

            bytes.Add(token);
            patternIndex++;
        }

        literal = bytes.ToArray();
        return true;
    }

    internal bool TryGetFixedPrefix(out byte[] prefix)
    {
        var bytes = new List<byte>();
        int patternIndex = 0;
        while (patternIndex < pattern.Length)
        {
            if (IsEscapedLiteral(pattern, patternIndex))
            {
                bytes.Add(pattern[patternIndex + 1]);
                patternIndex += 2;
                continue;
            }

            byte token = pattern[patternIndex];
            if (IsGlobMetacharacter(token))
            {
                break;
            }

            bytes.Add(token);
            patternIndex++;
        }

        prefix = bytes.ToArray();
        return prefix.Length != 0;
    }

    internal bool TryGetFixedSuffix(out byte[] suffix)
    {
        var bytes = new List<byte>();
        bool sawMetacharacter = false;
        int patternIndex = 0;
        while (patternIndex < pattern.Length)
        {
            if (IsEscapedLiteral(pattern, patternIndex))
            {
                bytes.Add(pattern[patternIndex + 1]);
                patternIndex += 2;
                continue;
            }

            byte token = pattern[patternIndex];
            if (token == (byte)'*' || token == (byte)'?')
            {
                sawMetacharacter = true;
                bytes.Clear();
                patternIndex++;
                continue;
            }

            if (token == (byte)'[')
            {
                int classEnd = FindClassEnd(pattern, patternIndex);
                if (classEnd > patternIndex)
                {
                    sawMetacharacter = true;
                    bytes.Clear();
                    patternIndex = classEnd + 1;
                    continue;
                }
            }

            if (token == (byte)'{')
            {
                int braceEnd = FindBraceEnd(pattern, patternIndex);
                if (braceEnd > patternIndex)
                {
                    sawMetacharacter = true;
                    bytes.Clear();
                    patternIndex = braceEnd + 1;
                    continue;
                }
            }

            bytes.Add(token);
            patternIndex++;
        }

        if (!sawMetacharacter)
        {
            suffix = [];
            return false;
        }

        suffix = bytes.ToArray();
        return suffix.Length != 0;
    }

    internal bool TryGetExtensionOnly(out byte[] extension)
    {
        extension = [];
        if (options.AsciiCaseInsensitive)
        {
            return false;
        }

        int patternIndex = StartsWithRecursivePrefix() ? 3 : 0;
        if (patternIndex >= pattern.Length || pattern[patternIndex] != (byte)'*')
        {
            return false;
        }

        if (patternIndex == 0 && options.LiteralSeparator)
        {
            return false;
        }

        patternIndex++;
        if (patternIndex >= pattern.Length || pattern[patternIndex] != (byte)'.')
        {
            return false;
        }

        var bytes = new List<byte> { (byte)'.' };
        patternIndex++;
        while (patternIndex < pattern.Length)
        {
            if (IsEscapedLiteral(pattern, patternIndex))
            {
                byte escaped = pattern[patternIndex + 1];
                if (escaped == (byte)'.' || IsSeparator(escaped))
                {
                    return false;
                }

                bytes.Add(escaped);
                patternIndex += 2;
                continue;
            }

            byte token = pattern[patternIndex];
            if (token == (byte)'.' || IsSeparator(token) || IsGlobMetacharacter(token))
            {
                return false;
            }

            bytes.Add(token);
            patternIndex++;
        }

        extension = bytes.ToArray();
        return extension.Length > 1;
    }

    internal bool TryGetRequiredExtension(out byte[] extension)
    {
        extension = [];
        if (options.AsciiCaseInsensitive)
        {
            return false;
        }

        var reversed = new List<byte>();
        int patternIndex = pattern.Length - 1;
        while (patternIndex >= 0)
        {
            byte token = pattern[patternIndex];
            if (IsEscapedPatternIndex(patternIndex))
            {
                if (IsSeparator(token))
                {
                    return false;
                }

                reversed.Add(token);
                if (token == (byte)'.')
                {
                    break;
                }

                patternIndex -= 2;
                continue;
            }

            if (IsSeparator(token) || IsReverseGlobMetacharacter(token))
            {
                return false;
            }

            reversed.Add(token);
            if (token == (byte)'.')
            {
                break;
            }

            patternIndex--;
        }

        if (reversed.Count <= 1 || reversed[^1] != (byte)'.')
        {
            return false;
        }

        extension = new byte[reversed.Count];
        for (int index = 0; index < reversed.Count; index++)
        {
            extension[index] = reversed[reversed.Count - index - 1];
        }

        return true;
    }

    internal bool TryGetComponentSuffix(out byte[] suffix, out byte[] exactLiteral)
    {
        suffix = [];
        exactLiteral = [];
        if (options.AsciiCaseInsensitive || !StartsWithRecursivePrefix())
        {
            return false;
        }

        int patternIndex = 3;
        if (patternIndex >= pattern.Length || IsGlobMetacharacter(pattern[patternIndex]))
        {
            return false;
        }

        var suffixBytes = new List<byte> { (byte)'/' };
        var literalBytes = new List<byte>();
        while (patternIndex < pattern.Length)
        {
            if (IsEscapedLiteral(pattern, patternIndex))
            {
                byte escaped = pattern[patternIndex + 1];
                suffixBytes.Add(escaped);
                literalBytes.Add(escaped);
                patternIndex += 2;
                continue;
            }

            byte token = pattern[patternIndex];
            if (IsGlobMetacharacter(token))
            {
                return false;
            }

            suffixBytes.Add(token);
            literalBytes.Add(token);
            patternIndex++;
        }

        if (literalBytes.Count == 0)
        {
            return false;
        }

        suffix = suffixBytes.ToArray();
        exactLiteral = literalBytes.ToArray();
        return true;
    }

    internal bool HasPathSeparator()
    {
        return PatternContainsSeparator();
    }

    internal bool LiteralEquals(ReadOnlySpan<byte> path, ReadOnlySpan<byte> literal)
    {
        return SpanEquals(path, literal);
    }

    internal bool BaseNameLiteralEquals(ReadOnlySpan<byte> path, ReadOnlySpan<byte> literal)
    {
        return SpanEquals(GetBaseName(path), literal);
    }

    internal bool BaseNameStartsWith(ReadOnlySpan<byte> path, ReadOnlySpan<byte> prefix)
    {
        ReadOnlySpan<byte> baseName = GetBaseName(path);
        return baseName.Length >= prefix.Length && SpanEquals(baseName[..prefix.Length], prefix);
    }

    internal bool PathEndsWith(ReadOnlySpan<byte> path, ReadOnlySpan<byte> suffix)
    {
        return path.Length >= suffix.Length && SpanEquals(path[^suffix.Length..], suffix);
    }

    internal bool PathExtensionEquals(ReadOnlySpan<byte> path, ReadOnlySpan<byte> extension)
    {
        return SpanEquals(GetExtension(GetBaseName(path)), extension);
    }

    internal bool BaseNameExtensionEquals(ReadOnlySpan<byte> baseName, ReadOnlySpan<byte> extension)
    {
        return SpanEquals(GetExtension(baseName), extension);
    }

    internal bool FixedPrefixAppliesToBaseName()
    {
        return options.MatchBaseName && !PatternContainsSeparator();
    }

    internal bool IsAsciiCaseInsensitive()
    {
        return options.AsciiCaseInsensitive;
    }

    internal bool IsExtensionSuffix(ReadOnlySpan<byte> suffix)
    {
        return suffix.Length > 1 && suffix[0] == (byte)'.' && !ContainsSeparator(suffix[1..]);
    }

    private bool Match(ReadOnlySpan<byte> currentPattern, int patternIndex, ReadOnlySpan<byte> path, int pathIndex)
    {
        while (patternIndex < currentPattern.Length)
        {
            byte token = currentPattern[patternIndex];
            if (IsEscapedLiteral(currentPattern, patternIndex))
            {
                if (!MatchLiteral(currentPattern[patternIndex + 1], path, pathIndex))
                {
                    return false;
                }

                patternIndex += 2;
                pathIndex++;
                continue;
            }

            if (token == (byte)'*')
            {
                bool recursive = IsRecursiveDoubleStar(currentPattern, patternIndex);
                int nextPatternIndex = recursive ? patternIndex + 2 : patternIndex + 1;
                if (recursive
                    && nextPatternIndex < currentPattern.Length
                    && IsSeparator(currentPattern[nextPatternIndex])
                    && Match(currentPattern, nextPatternIndex + 1, path, pathIndex))
                {
                    return true;
                }

                if (MatchStar(currentPattern, nextPatternIndex, path, pathIndex, recursive))
                {
                    return true;
                }

                return false;
            }

            if (token == (byte)'?')
            {
                if (pathIndex >= path.Length || IsBlockedSeparator(path[pathIndex]))
                {
                    return false;
                }

                patternIndex++;
                pathIndex++;
                continue;
            }

            if (token == (byte)'[')
            {
                int classEnd = FindClassEnd(currentPattern, patternIndex);
                if (classEnd > patternIndex)
                {
                    if (pathIndex >= path.Length)
                    {
                        return false;
                    }

                    if (!MatchClass(currentPattern[(patternIndex + 1)..classEnd], path[pathIndex]))
                    {
                        return false;
                    }

                    patternIndex = classEnd + 1;
                    pathIndex++;
                    continue;
                }
            }

            if (token == (byte)'{')
            {
                int braceEnd = FindBraceEnd(currentPattern, patternIndex);
                if (braceEnd > patternIndex)
                {
                    if (MatchAlternatives(currentPattern, patternIndex, braceEnd, path, pathIndex))
                    {
                        return true;
                    }

                    return false;
                }
            }

            if (!MatchLiteral(token, path, pathIndex))
            {
                return false;
            }

            patternIndex++;
            pathIndex++;
        }

        return pathIndex == path.Length;
    }

    private static void ValidatePattern(ReadOnlySpan<byte> currentPattern, GlobOptions options)
    {
        int alternates = 0;
        bool foundUnclosedClass = false;
        for (int index = 0; index < currentPattern.Length; index++)
        {
            byte value = currentPattern[index];
            if (options.BackslashEscapes && value == (byte)'\\')
            {
                if (index + 1 >= currentPattern.Length)
                {
                    throw new GlobParseException(GlobParseErrorKind.DanglingEscape, currentPattern);
                }

                index++;
                continue;
            }

            if (value == (byte)'[' && !foundUnclosedClass)
            {
                int classEnd = ValidateClass(currentPattern, index, options, out bool unclosedAllowed);
                if (unclosedAllowed)
                {
                    foundUnclosedClass = true;
                    continue;
                }

                index = classEnd;
                continue;
            }

            if (value == (byte)'{')
            {
                alternates++;
                continue;
            }

            if (value == (byte)'}')
            {
                if (alternates == 0)
                {
                    throw new GlobParseException(GlobParseErrorKind.UnopenedAlternates, currentPattern);
                }

                alternates--;
            }
        }

        if (alternates != 0)
        {
            throw new GlobParseException(GlobParseErrorKind.UnclosedAlternates, currentPattern);
        }
    }

    private static int ValidateClass(
        ReadOnlySpan<byte> currentPattern,
        int classStart,
        GlobOptions options,
        out bool unclosedAllowed)
    {
        unclosedAllowed = false;
        int index = classStart + 1;
        if (index < currentPattern.Length && (currentPattern[index] == (byte)'!' || currentPattern[index] == (byte)'^'))
        {
            index++;
        }

        bool first = true;
        bool inRange = false;
        byte rangeStart = 0;
        while (index < currentPattern.Length)
        {
            byte value = currentPattern[index];
            if (value == (byte)']')
            {
                if (first)
                {
                    rangeStart = value;
                    first = false;
                    index++;
                    continue;
                }

                return index;
            }

            if (value == (byte)'-')
            {
                if (first)
                {
                    rangeStart = value;
                    first = false;
                    index++;
                    continue;
                }

                if (inRange)
                {
                    ValidateRange(currentPattern, rangeStart, value);
                    inRange = false;
                    index++;
                    continue;
                }

                inRange = true;
                index++;
                continue;
            }

            if (inRange)
            {
                ValidateRange(currentPattern, rangeStart, value);
                inRange = false;
            }
            else
            {
                rangeStart = value;
            }

            first = false;
            index++;
        }

        if (options.AllowUnclosedClass)
        {
            unclosedAllowed = true;
            return -1;
        }

        throw new GlobParseException(GlobParseErrorKind.UnclosedClass, currentPattern);
    }

    private static void ValidateRange(ReadOnlySpan<byte> currentPattern, byte start, byte end)
    {
        if (end < start)
        {
            throw new GlobParseException(GlobParseErrorKind.InvalidRange, currentPattern, start, end);
        }
    }

    private bool IsRecursiveDoubleStar(ReadOnlySpan<byte> currentPattern, int patternIndex)
    {
        if (patternIndex + 1 >= currentPattern.Length || currentPattern[patternIndex + 1] != (byte)'*')
        {
            return false;
        }

        bool atComponentStart = patternIndex == 0 || IsSeparator(currentPattern[patternIndex - 1]);
        if (!atComponentStart)
        {
            return false;
        }

        int nextIndex = patternIndex + 2;
        return nextIndex == currentPattern.Length || IsSeparator(currentPattern[nextIndex]);
    }

    private bool MatchStar(
        ReadOnlySpan<byte> currentPattern,
        int nextPatternIndex,
        ReadOnlySpan<byte> path,
        int pathIndex,
        bool recursive)
    {
        for (int candidate = pathIndex; candidate <= path.Length; candidate++)
        {
            if (!recursive && candidate > pathIndex && IsBlockedSeparator(path[candidate - 1]))
            {
                break;
            }

            if (Match(currentPattern, nextPatternIndex, path, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchAlternatives(
        ReadOnlySpan<byte> currentPattern,
        int braceStart,
        int braceEnd,
        ReadOnlySpan<byte> path,
        int pathIndex)
    {
        bool hasNonEmptyAlternative = HasNonEmptyAlternative(currentPattern, braceStart, braceEnd);
        int alternativeStart = braceStart + 1;
        int depth = 0;
        for (int index = braceStart + 1; index <= braceEnd; index++)
        {
            bool atEnd = index == braceEnd;
            byte value = atEnd ? (byte)',' : currentPattern[index];
            if (!atEnd && IsEscapedLiteral(currentPattern, index))
            {
                index++;
                continue;
            }

            if (!atEnd && value == (byte)'[')
            {
                int classEnd = FindClassEnd(currentPattern, index);
                if (classEnd > index)
                {
                    index = classEnd;
                    continue;
                }
            }

            if (!atEnd && value == (byte)'{')
            {
                depth++;
                continue;
            }

            if (!atEnd && value == (byte)'}')
            {
                depth--;
                continue;
            }

            if (value == (byte)',' && depth == 0)
            {
                ReadOnlySpan<byte> alternative = currentPattern[alternativeStart..index];
                if (ShouldEvaluateAlternative(alternative, hasNonEmptyAlternative))
                {
                    byte[] combined = Combine(alternative, currentPattern[(braceEnd + 1)..]);
                    if (Match(combined, patternIndex: 0, path, pathIndex))
                    {
                        return true;
                    }
                }

                alternativeStart = index + 1;
            }
        }

        return false;
    }

    private bool ShouldEvaluateAlternative(ReadOnlySpan<byte> alternative, bool hasNonEmptyAlternative)
    {
        return !alternative.IsEmpty || options.EmptyAlternates || !hasNonEmptyAlternative;
    }

    private bool HasNonEmptyAlternative(ReadOnlySpan<byte> currentPattern, int braceStart, int braceEnd)
    {
        int alternativeStart = braceStart + 1;
        int depth = 0;
        for (int index = braceStart + 1; index <= braceEnd; index++)
        {
            bool atEnd = index == braceEnd;
            byte value = atEnd ? (byte)',' : currentPattern[index];
            if (!atEnd && IsEscapedLiteral(currentPattern, index))
            {
                index++;
                continue;
            }

            if (!atEnd && value == (byte)'[')
            {
                int classEnd = FindClassEnd(currentPattern, index);
                if (classEnd > index)
                {
                    index = classEnd;
                    continue;
                }
            }

            if (!atEnd && value == (byte)'{')
            {
                depth++;
                continue;
            }

            if (!atEnd && value == (byte)'}')
            {
                depth--;
                continue;
            }

            if (value == (byte)',' && depth == 0)
            {
                if (index > alternativeStart)
                {
                    return true;
                }

                alternativeStart = index + 1;
            }
        }

        return false;
    }

    private bool MatchClass(ReadOnlySpan<byte> classPattern, byte value)
    {
        bool negated = classPattern.Length > 0 && (classPattern[0] == (byte)'!' || classPattern[0] == (byte)'^');
        int index = negated ? 1 : 0;
        bool matched = false;
        byte foldedValue = Fold(value);

        while (index < classPattern.Length)
        {
            byte start = classPattern[index];
            index++;

            if (index + 1 < classPattern.Length && classPattern[index] == (byte)'-')
            {
                byte end = classPattern[index + 1];
                index += 2;

                byte foldedStart = Fold(start);
                byte foldedEnd = Fold(end);
                if (foldedStart <= foldedValue && foldedValue <= foldedEnd)
                {
                    matched = true;
                }

                continue;
            }

            if (Fold(start) == foldedValue)
            {
                matched = true;
            }
        }

        return negated ? !matched : matched;
    }

    private bool MatchLiteral(byte value, ReadOnlySpan<byte> path, int pathIndex)
    {
        return pathIndex < path.Length && Fold(value) == Fold(path[pathIndex]);
    }

    private bool IsEscapedLiteral(ReadOnlySpan<byte> currentPattern, int patternIndex)
    {
        return options.BackslashEscapes
            && currentPattern[patternIndex] == (byte)'\\'
            && patternIndex + 1 < currentPattern.Length;
    }

    private bool IsBlockedSeparator(byte value)
    {
        return options.LiteralSeparator && IsSeparator(value);
    }

    private bool IsSeparator(byte value)
    {
        foreach (byte separator in options.PathSeparators)
        {
            if (value == separator)
            {
                return true;
            }
        }

        return false;
    }

    private bool PatternContainsSeparator()
    {
        return ContainsSeparator(pattern);
    }

    private bool StartsWithRecursivePrefix()
    {
        return pattern.Length >= 3
            && pattern[0] == (byte)'*'
            && pattern[1] == (byte)'*'
            && IsSeparator(pattern[2]);
    }

    private bool ContainsSeparator(ReadOnlySpan<byte> values)
    {
        foreach (byte value in values)
        {
            if (IsSeparator(value))
            {
                return true;
            }
        }

        return false;
    }

    private bool SpanEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (Fold(left[index]) != Fold(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private ReadOnlySpan<byte> GetBaseName(ReadOnlySpan<byte> path)
    {
        for (int index = path.Length - 1; index >= 0; index--)
        {
            if (IsSeparator(path[index]))
            {
                return path[(index + 1)..];
            }
        }

        return path;
    }

    private static ReadOnlySpan<byte> GetExtension(ReadOnlySpan<byte> baseName)
    {
        if (baseName.IsEmpty)
        {
            return [];
        }

        for (int index = baseName.Length - 1; index >= 0; index--)
        {
            if (baseName[index] == (byte)'.')
            {
                return baseName[index..];
            }
        }

        return [];
    }

    private byte Fold(byte value)
    {
        return options.AsciiCaseInsensitive && value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 0x20)
            : value;
    }

    private static int FindClassEnd(ReadOnlySpan<byte> currentPattern, int patternIndex)
    {
        int start = patternIndex + 1;
        if (start < currentPattern.Length && (currentPattern[start] == (byte)'!' || currentPattern[start] == (byte)'^'))
        {
            start++;
        }

        if (start < currentPattern.Length && currentPattern[start] == (byte)']')
        {
            start++;
        }

        for (int index = start; index < currentPattern.Length; index++)
        {
            if (currentPattern[index] == (byte)']')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindBraceEnd(ReadOnlySpan<byte> currentPattern, int patternIndex)
    {
        int depth = 0;
        for (int index = patternIndex; index < currentPattern.Length; index++)
        {
            if (currentPattern[index] == (byte)'\\' && index + 1 < currentPattern.Length)
            {
                index++;
                continue;
            }

            if (currentPattern[index] == (byte)'[')
            {
                int classEnd = FindClassEnd(currentPattern, index);
                if (classEnd > index)
                {
                    index = classEnd;
                    continue;
                }
            }

            if (currentPattern[index] == (byte)'{')
            {
                depth++;
                continue;
            }

            if (currentPattern[index] == (byte)'}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static bool IsGlobMetacharacter(byte token)
    {
        return token is (byte)'*' or (byte)'?' or (byte)'[' or (byte)'{';
    }

    private static bool IsReverseGlobMetacharacter(byte token)
    {
        return token is (byte)'*' or (byte)'?' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}';
    }

    private bool IsEscapedPatternIndex(int patternIndex)
    {
        if (!options.BackslashEscapes)
        {
            return false;
        }

        int backslashCount = 0;
        for (int index = patternIndex - 1; index >= 0 && pattern[index] == (byte)'\\'; index--)
        {
            backslashCount++;
        }

        return backslashCount % 2 != 0;
    }

    private static byte[] Combine(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        byte[] combined = new byte[left.Length + right.Length];
        left.CopyTo(combined);
        right.CopyTo(combined.AsSpan(left.Length));
        return combined;
    }

    private static void AppendAnyBytes(List<byte> bytes)
    {
        bytes.Add((byte)'.');
        bytes.Add((byte)'*');
    }

    private static void AppendHexLiteral(List<byte> bytes, byte value)
    {
        bytes.Add((byte)'\\');
        bytes.Add((byte)'x');
        bytes.Add(GetLowerHexDigit(value >> 4));
        bytes.Add(GetLowerHexDigit(value & 0x0f));
    }

    private static byte GetLowerHexDigit(int value)
    {
        return (byte)(value < 10 ? (byte)'0' + value : (byte)'a' + value - 10);
    }

    private static void AppendAscii(List<byte> bytes, ReadOnlySpan<byte> value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            bytes.Add(value[index]);
        }
    }
}
