using System.Text;

namespace Scout;

/// <summary>
/// Prepares and validates command-line patterns before regex compilation.
/// </summary>
internal static class PatternPreparation
{
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private const ulong RegexCompiledBaseSize = 64;
    private const ulong RegexCompiledByteSize = 16;
    private const ulong RegexCompiledUnicodeDigitClassSize = 2_048;
    private const ulong RegexCompiledUnicodeNegatedDigitClassSize = 16_384;
    private const ulong RegexCompiledUnicodeWordClassSize = 16_384;
    private const ulong RegexCompiledUnicodeWhitespaceClassSize = 512;
    private const ulong RegexCompiledUnicodeNegatedWhitespaceClassSize = 2_048;

    /// <summary>
    /// Converts an operating-system string pattern to UTF-8 bytes.
    /// </summary>
    /// <param name="pattern">The pattern to convert.</param>
    /// <param name="bytes">Receives the UTF-8 pattern bytes.</param>
    /// <returns><see langword="true" /> when the pattern can be represented as text.</returns>
    public static bool TryGetPatternBytes(OsString pattern, out byte[] bytes)
    {
        if (!pattern.TryGetText(out string text))
        {
            bytes = [];
            return false;
        }

        bytes = s_utf8.GetBytes(text);
        return true;
    }

    /// <summary>
    /// Determines whether a pattern set contains the configured record terminator.
    /// </summary>
    /// <param name="patterns">The patterns to inspect.</param>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <param name="fixedStrings">Whether patterns are fixed strings.</param>
    /// <returns><see langword="true" /> when a pattern contains the record terminator.</returns>
    public static bool ContainsLineTerminator(List<byte[]> patterns, bool nullData, bool fixedStrings)
    {
        byte terminator = nullData ? (byte)0 : (byte)'\n';
        for (int index = 0; index < patterns.Count; index++)
        {
            if (fixedStrings)
            {
                if (patterns[index].AsSpan().Contains(terminator))
                {
                    return true;
                }

                continue;
            }

            if (nullData ? ContainsRegexNulLiteral(patterns[index]) : ContainsLiteralLineFeed(patterns[index]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether any regex pattern contains an explicit NUL literal.
    /// </summary>
    /// <param name="patterns">The patterns to inspect.</param>
    /// <returns><see langword="true" /> when a pattern contains an explicit NUL literal.</returns>
    public static bool ContainsRegexNulLiteral(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ContainsRegexNulLiteral(patterns[index]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the diagnostic emitted when a line-oriented regex contains its record terminator.
    /// </summary>
    /// <param name="nullData">Whether NUL terminates records.</param>
    /// <returns>The diagnostic message.</returns>
    public static string BuildLineTerminatorPatternError(bool nullData)
    {
        string literal = nullData ? "\\0" : "\\n";
        return "the literal \"" + literal + "\" is not allowed in a regex\n\n" +
            "Consider enabling multiline mode with the --multiline flag (or -U for short).\n" +
            "When multiline mode is enabled, new line characters can be matched.";
    }

    /// <summary>
    /// Determines whether patterns require whole-buffer multiline execution.
    /// </summary>
    /// <param name="patterns">The parsed regex patterns.</param>
    /// <param name="multilineDotall">Whether dotall is enabled before inline flags are applied.</param>
    /// <returns><see langword="true" /> when line-oriented execution cannot preserve matcher semantics.</returns>
    public static bool ShouldUseMultilineRegex(IReadOnlyList<byte[]> patterns, bool multilineDotall)
    {
        return RegexSyntaxAnalysis.CanMatchLineFeed(patterns, multilineDotall) ||
            RegexSyntaxAnalysis.RequiresWholeHaystack(patterns);
    }

    /// <summary>
    /// Determines whether JSON output requires whole-buffer multiline execution.
    /// </summary>
    /// <param name="patterns">The parsed regex patterns.</param>
    /// <param name="multilineDotall">Whether dotall is enabled before inline flags are applied.</param>
    /// <returns><see langword="true" /> when JSON line reporting requires multiline execution.</returns>
    public static bool ShouldUseJsonMultilineRegex(IReadOnlyList<byte[]> patterns, bool multilineDotall)
    {
        return ShouldUseMultilineRegex(patterns, multilineDotall) || RegexSyntaxAnalysis.CanMatchAnchorLineBoundary(patterns);
    }

    private static bool ContainsLiteralLineFeed(ReadOnlySpan<byte> pattern)
    {
        var scopeDepths = new List<int>();
        var scopeValues = new List<bool>();
        bool ignoreWhitespace = false;
        bool inClass = false;
        int depth = 0;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (inClass)
            {
                if (value == (byte)'\\' && index + 1 < pattern.Length)
                {
                    index++;
                }
                else if (value == (byte)']')
                {
                    inClass = false;
                }

                continue;
            }

            if (value == (byte)'\\')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == (byte)'n')
                {
                    return true;
                }

                index++;
                continue;
            }

            if (value == (byte)'[')
            {
                inClass = true;
                continue;
            }

            if (ignoreWhitespace)
            {
                if (value == (byte)'#')
                {
                    index = SkipRegexComment(pattern, index);
                    continue;
                }

                if (IsRegexWhitespace(value))
                {
                    continue;
                }
            }

            if (value == (byte)'\n')
            {
                return true;
            }

            if (value == (byte)'(')
            {
                if (index + 1 < pattern.Length &&
                    pattern[index + 1] == (byte)'?' &&
                    TryReadInlineFlagGroup(pattern, index, out int markerIndex, out bool scoped, out bool? scopedIgnoreWhitespace))
                {
                    if (scoped)
                    {
                        depth++;
                        if (scopedIgnoreWhitespace.HasValue)
                        {
                            scopeDepths.Add(depth);
                            scopeValues.Add(ignoreWhitespace);
                            ignoreWhitespace = scopedIgnoreWhitespace.Value;
                        }
                    }
                    else if (scopedIgnoreWhitespace.HasValue)
                    {
                        ignoreWhitespace = scopedIgnoreWhitespace.Value;
                    }

                    index = markerIndex;
                    continue;
                }

                depth++;
                continue;
            }

            if (value == (byte)')')
            {
                while (scopeDepths.Count > 0 && scopeDepths[^1] == depth)
                {
                    ignoreWhitespace = scopeValues[^1];
                    scopeDepths.RemoveAt(scopeDepths.Count - 1);
                    scopeValues.RemoveAt(scopeValues.Count - 1);
                }

                if (depth > 0)
                {
                    depth--;
                }
            }
        }

        return false;
    }

    private static bool ContainsRegexNulLiteral(ReadOnlySpan<byte> pattern)
    {
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == 0)
            {
                return true;
            }

            if (value == (byte)'[' && TryFindRegexClassEnd(pattern, index, out int classEnd))
            {
                if (RegexClassMatchesOnlyNul(pattern[(index + 1)..classEnd]))
                {
                    return true;
                }

                index = classEnd;
                continue;
            }

            if (value != (byte)'\\' || index + 1 >= pattern.Length)
            {
                continue;
            }

            byte escaped = pattern[index + 1];
            if (escaped == (byte)'x')
            {
                if (index + 3 < pattern.Length && TryReadHexByte(pattern[index + 2], pattern[index + 3], out byte byteValue))
                {
                    if (byteValue == 0)
                    {
                        return true;
                    }

                    index += 3;
                    continue;
                }

                if (TryReadBracedHexScalar(pattern, index + 2, out int scalarValue, out int endIndex))
                {
                    if (scalarValue == 0)
                    {
                        return true;
                    }

                    index = endIndex;
                    continue;
                }
            }
            else if (escaped == (byte)'u' && TryReadBracedHexScalar(pattern, index + 2, out int scalarValue, out int endIndex))
            {
                if (scalarValue == 0)
                {
                    return true;
                }

                index = endIndex;
                continue;
            }

            index++;
        }

        return false;
    }

    private static bool TryFindRegexClassEnd(ReadOnlySpan<byte> pattern, int classStart, out int classEnd)
    {
        classEnd = -1;
        for (int index = classStart + 1; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\\' && index + 1 < pattern.Length)
            {
                index++;
                continue;
            }

            if (pattern[index] == (byte)']')
            {
                classEnd = index;
                return true;
            }
        }

        return false;
    }

    private static bool RegexClassMatchesOnlyNul(ReadOnlySpan<byte> expression)
    {
        Span<bool> positive = stackalloc bool[256];
        int index = 0;
        bool negated = !expression.IsEmpty && expression[0] == (byte)'^';
        if (negated)
        {
            index++;
        }

        while (index < expression.Length)
        {
            if (!TryReadRegexClassByteToken(expression, ref index, out byte start))
            {
                return false;
            }

            if (index + 1 < expression.Length && expression[index] == (byte)'-')
            {
                int rangeEndIndex = index + 1;
                if (TryReadRegexClassByteToken(expression, ref rangeEndIndex, out byte end))
                {
                    AddRegexClassByteRange(positive, start, end);
                    index = rangeEndIndex;
                    continue;
                }
            }

            positive[start] = true;
        }

        bool matchesNul = negated ? !positive[0] : positive[0];
        if (!matchesNul)
        {
            return false;
        }

        for (int byteValue = 1; byteValue < positive.Length; byteValue++)
        {
            bool matches = negated ? !positive[byteValue] : positive[byteValue];
            if (matches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadRegexClassByteToken(ReadOnlySpan<byte> expression, ref int index, out byte value)
    {
        value = 0;
        if (index >= expression.Length)
        {
            return false;
        }

        byte token = expression[index];
        index++;
        if (token != (byte)'\\' || index >= expression.Length)
        {
            value = token;
            return true;
        }

        byte escaped = expression[index];
        index++;
        if (escaped == (byte)'x')
        {
            if (index + 1 < expression.Length && TryReadHexByte(expression[index], expression[index + 1], out value))
            {
                index += 2;
                return true;
            }

            if (TryReadBracedHexScalar(expression, index, out int scalarValue, out int endIndex) && scalarValue <= byte.MaxValue)
            {
                value = (byte)scalarValue;
                index = endIndex + 1;
                return true;
            }

            return false;
        }

        if (escaped == (byte)'u' &&
            TryReadBracedHexScalar(expression, index, out int unicodeScalarValue, out int unicodeEndIndex) &&
            unicodeScalarValue <= byte.MaxValue)
        {
            value = (byte)unicodeScalarValue;
            index = unicodeEndIndex + 1;
            return true;
        }

        value = escaped switch
        {
            (byte)'0' => (byte)0,
            (byte)'a' => 0x07,
            (byte)'f' => 0x0C,
            (byte)'n' => (byte)'\n',
            (byte)'r' => (byte)'\r',
            (byte)'t' => (byte)'\t',
            (byte)'v' => 0x0B,
            _ => escaped,
        };
        return true;
    }

    private static void AddRegexClassByteRange(Span<bool> matches, byte start, byte end)
    {
        byte lower = Math.Min(start, end);
        byte upper = Math.Max(start, end);
        for (int byteValue = lower; byteValue <= upper; byteValue++)
        {
            matches[byteValue] = true;
        }
    }

    private static bool TryReadHexByte(byte high, byte low, out byte value)
    {
        value = 0;
        if (!TryGetHexDigit(high, out int highValue) || !TryGetHexDigit(low, out int lowValue))
        {
            return false;
        }

        value = (byte)((highValue << 4) | lowValue);
        return true;
    }

    private static bool TryReadBracedHexScalar(ReadOnlySpan<byte> pattern, int openBraceIndex, out int value, out int endIndex)
    {
        value = 0;
        endIndex = openBraceIndex;
        if (openBraceIndex >= pattern.Length || pattern[openBraceIndex] != (byte)'{')
        {
            return false;
        }

        int index = openBraceIndex + 1;
        int digits = 0;
        while (index < pattern.Length && pattern[index] != (byte)'}')
        {
            if (!TryGetHexDigit(pattern[index], out int digit))
            {
                return false;
            }

            value = (value * 16) + digit;
            digits++;
            index++;
        }

        if (digits == 0 || index >= pattern.Length || pattern[index] != (byte)'}')
        {
            return false;
        }

        endIndex = index;
        return true;
    }

    private static bool TryGetHexDigit(byte value, out int digit)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            digit = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            digit = value - (byte)'a' + 10;
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            digit = value - (byte)'A' + 10;
            return true;
        }

        digit = 0;
        return false;
    }

    private static int SkipRegexComment(ReadOnlySpan<byte> pattern, int commentStart)
    {
        for (int index = commentStart + 1; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\n')
            {
                return index;
            }
        }

        return pattern.Length - 1;
    }

    private static bool TryReadInlineFlagGroup(
        ReadOnlySpan<byte> pattern,
        int openParenIndex,
        out int markerIndex,
        out bool scoped,
        out bool? ignoreWhitespace)
    {
        markerIndex = openParenIndex;
        scoped = false;
        ignoreWhitespace = null;
        bool negated = false;
        bool sawFlag = false;
        for (int index = openParenIndex + 2; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'-')
            {
                negated = true;
                continue;
            }

            if (IsInlineRegexFlag(value))
            {
                sawFlag = true;
                if (value == (byte)'x')
                {
                    ignoreWhitespace = !negated;
                }

                continue;
            }

            if (value == (byte)':')
            {
                markerIndex = index;
                scoped = true;
                return true;
            }

            if (value == (byte)')')
            {
                markerIndex = index;
                return sawFlag;
            }

            return false;
        }

        return false;
    }

    private static bool IsInlineRegexFlag(byte value)
    {
        return value is (byte)'i' or (byte)'m' or (byte)'s' or (byte)'U' or (byte)'u' or (byte)'x' or (byte)'R';
    }

    private static bool IsRegexWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0C;
    }

    /// <summary>
    /// Escapes regex metacharacters in every fixed-string pattern.
    /// </summary>
    /// <param name="patterns">The patterns to escape in place.</param>
    public static void EscapeFixedStringPatterns(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            patterns[index] = EscapeFixedStringPattern(patterns[index]);
        }
    }

    private static byte[] EscapeFixedStringPattern(byte[] pattern)
    {
        int escapeCount = 0;
        for (int index = 0; index < pattern.Length; index++)
        {
            if (IsRegexMetaByte(pattern[index]))
            {
                escapeCount++;
            }
        }

        if (escapeCount == 0)
        {
            return pattern;
        }

        byte[] escaped = new byte[pattern.Length + escapeCount];
        int outputIndex = 0;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (IsRegexMetaByte(value))
            {
                escaped[outputIndex] = (byte)'\\';
                outputIndex++;
            }

            escaped[outputIndex] = value;
            outputIndex++;
        }

        return escaped;
    }

    /// <summary>
    /// Wraps non-ASCII patterns so outer transformations preserve their scope.
    /// </summary>
    /// <param name="patterns">The patterns to update in place.</param>
    public static void WrapNonAsciiPatterns(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ContainsNonAscii(patterns[index]))
            {
                patterns[index] = WrapNonCapturingGroup(patterns[index]);
            }
        }
    }

    /// <summary>
    /// Disables Unicode mode around every pattern.
    /// </summary>
    /// <param name="patterns">The patterns to update in place.</param>
    public static void WrapNoUnicodePatterns(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            patterns[index] = WrapNoUnicodeGroup(patterns[index]);
        }
    }

    /// <summary>
    /// Wraps patterns whose top-level syntax requires a non-capturing scope.
    /// </summary>
    /// <param name="patterns">The patterns to update in place.</param>
    public static void WrapRegexPatterns(List<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ShouldWrapRegexPattern(patterns[index]))
            {
                patterns[index] = WrapNonCapturingGroup(patterns[index]);
            }
        }
    }

    private static bool ShouldWrapRegexPattern(ReadOnlySpan<byte> pattern)
    {
        int rawDepth = 0;
        int wrappedDepth = 1;
        bool rawUnderflow = false;
        bool inClass = false;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (inClass)
            {
                if (value == (byte)'\\' && index + 1 < pattern.Length)
                {
                    index++;
                }
                else if (value == (byte)']')
                {
                    inClass = false;
                }

                continue;
            }

            if (value == (byte)'\\')
            {
                index++;
                continue;
            }

            if (value == (byte)'[')
            {
                inClass = true;
                continue;
            }

            if (value == (byte)'(')
            {
                rawDepth++;
                wrappedDepth++;
                continue;
            }

            if (value != (byte)')')
            {
                continue;
            }

            rawDepth--;
            if (rawDepth < 0)
            {
                rawUnderflow = true;
            }

            wrappedDepth--;
            if (wrappedDepth < 0)
            {
                return false;
            }
        }

        return rawUnderflow && wrappedDepth == 1;
    }

    private static byte[] WrapNonCapturingGroup(byte[] pattern)
    {
        byte[] wrapped = new byte[pattern.Length + 4];
        wrapped[0] = (byte)'(';
        wrapped[1] = (byte)'?';
        wrapped[2] = (byte)':';
        pattern.CopyTo(wrapped.AsSpan(3));
        wrapped[^1] = (byte)')';
        return wrapped;
    }

    private static byte[] WrapNoUnicodeGroup(byte[] pattern)
    {
        byte[] wrapped = new byte[pattern.Length + 6];
        wrapped[0] = (byte)'(';
        wrapped[1] = (byte)'?';
        wrapped[2] = (byte)'-';
        wrapped[3] = (byte)'u';
        wrapped[4] = (byte)':';
        pattern.CopyTo(wrapped.AsSpan(5));
        wrapped[^1] = (byte)')';
        return wrapped;
    }

    private static bool ContainsNonAscii(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] >= 0x80)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a byte has regex metacharacter meaning.
    /// </summary>
    /// <param name="value">The byte to inspect.</param>
    /// <returns><see langword="true" /> when the byte must be escaped in a fixed string.</returns>
    public static bool IsRegexMetaByte(byte value)
    {
        return value is (byte)'\\'
            or (byte)'.'
            or (byte)'['
            or (byte)']'
            or (byte)'('
            or (byte)')'
            or (byte)'{'
            or (byte)'}'
            or (byte)'*'
            or (byte)'+'
            or (byte)'?'
            or (byte)'^'
            or (byte)'$'
            or (byte)'|';
    }

    /// <summary>
    /// Validates that repetition operators have preceding expressions.
    /// </summary>
    /// <param name="patterns">The patterns to validate.</param>
    /// <param name="diagnostics">The diagnostic destination.</param>
    /// <returns><see langword="true" /> when every repetition is valid.</returns>
    public static bool TryValidateRegexRepetitionExpressions(List<byte[]> patterns, DiagnosticMessenger diagnostics)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (TryFindMissingRepetitionExpression(patterns[index], out int offset))
            {
                diagnostics.ErrorMessage(new ScoutError(BuildRegexParseError(
                    patterns[index],
                    offset,
                    "repetition operator missing expression")).WithContext(ScoutErrorContext.ProgramContext()));
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates the configured compiled-regex size limit.
    /// </summary>
    /// <param name="patterns">The patterns to estimate.</param>
    /// <param name="lowArgs">The parsed low-level arguments.</param>
    /// <param name="diagnostics">The diagnostic destination.</param>
    /// <returns><see langword="true" /> when the estimated compiled size is within the limit.</returns>
    public static bool TryValidateRegexSizeLimit(List<byte[]> patterns, CliLowArgs lowArgs, DiagnosticMessenger diagnostics)
    {
        if (lowArgs.RegexSizeLimit is not ulong limit)
        {
            return true;
        }

        ulong compiledSize = 0;
        for (int index = 0; index < patterns.Count; index++)
        {
            compiledSize = SaturatingAdd(compiledSize, EstimateCompiledRegexSize(patterns[index], lowArgs.Unicode));
            if (compiledSize > limit)
            {
                diagnostics.ErrorMessage(new ScoutError($"compiled regex exceeds size limit of {limit}").WithContext(ScoutErrorContext.ProgramContext()));
                return false;
            }
        }

        return true;
    }

    private static ulong EstimateCompiledRegexSize(ReadOnlySpan<byte> pattern, bool unicode)
    {
        ulong size = SaturatingAdd(RegexCompiledBaseSize, SaturatingMultiply((ulong)pattern.Length, RegexCompiledByteSize));
        for (int index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\\' && index + 1 < pattern.Length)
            {
                size = SaturatingAdd(size, GetEscapedClassCompiledSize(pattern[index + 1], unicode));
                index++;
            }
        }

        return size;
    }

    private static ulong GetEscapedClassCompiledSize(byte escaped, bool unicode)
    {
        if (!unicode)
        {
            return 0;
        }

        return escaped switch
        {
            (byte)'d' => RegexCompiledUnicodeDigitClassSize,
            (byte)'D' => RegexCompiledUnicodeNegatedDigitClassSize,
            (byte)'w' or (byte)'W' => RegexCompiledUnicodeWordClassSize,
            (byte)'s' => RegexCompiledUnicodeWhitespaceClassSize,
            (byte)'S' => RegexCompiledUnicodeNegatedWhitespaceClassSize,
            _ => 0,
        };
    }

    private static ulong SaturatingAdd(ulong left, ulong right)
    {
        return ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    }

    private static ulong SaturatingMultiply(ulong left, ulong right)
    {
        if (left == 0 || right == 0)
        {
            return 0;
        }

        return left > ulong.MaxValue / right ? ulong.MaxValue : left * right;
    }

    private static bool TryFindMissingRepetitionExpression(ReadOnlySpan<byte> pattern, out int offset)
    {
        bool expectingExpression = true;
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (expectingExpression && IsRegexRepetitionOperator(value))
            {
                offset = index;
                return true;
            }

            if (value == (byte)'\\')
            {
                index = SkipRegexEscape(pattern, index);
                expectingExpression = false;
                continue;
            }

            if (value == (byte)'[')
            {
                index = SkipRegexCharacterClass(pattern, index);
                expectingExpression = false;
                continue;
            }

            if (value == (byte)'|')
            {
                expectingExpression = true;
                continue;
            }

            if (value == (byte)')')
            {
                expectingExpression = false;
                continue;
            }

            if (value == (byte)'(')
            {
                if (TryReadRegexGroupPrefix(pattern, index, out int contentStart))
                {
                    index = contentStart - 1;
                    expectingExpression = true;
                    continue;
                }

                expectingExpression = true;
                continue;
            }

            if (!expectingExpression && IsRegexRepetitionOperator(value))
            {
                index = SkipRegexRepetition(pattern, index);
                continue;
            }

            expectingExpression = false;
        }

        offset = -1;
        return false;
    }

    private static bool IsRegexRepetitionOperator(byte value)
    {
        return value is (byte)'?' or (byte)'*' or (byte)'+' or (byte)'{';
    }

    private static int SkipRegexEscape(ReadOnlySpan<byte> pattern, int escapeIndex)
    {
        if (escapeIndex + 2 < pattern.Length &&
            (pattern[escapeIndex + 1] == (byte)'x' || pattern[escapeIndex + 1] == (byte)'u') &&
            pattern[escapeIndex + 2] == (byte)'{')
        {
            for (int index = escapeIndex + 3; index < pattern.Length; index++)
            {
                if (pattern[index] == (byte)'}')
                {
                    return index;
                }
            }
        }

        return Math.Min(escapeIndex + 1, pattern.Length - 1);
    }

    private static int SkipRegexCharacterClass(ReadOnlySpan<byte> pattern, int classStart)
    {
        for (int index = classStart + 1; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'\\')
            {
                index++;
                continue;
            }

            if (pattern[index] == (byte)']')
            {
                return index;
            }
        }

        return pattern.Length - 1;
    }

    private static bool TryReadRegexGroupPrefix(
        ReadOnlySpan<byte> pattern,
        int groupStart,
        out int contentStart)
    {
        contentStart = groupStart + 1;
        if (groupStart + 1 >= pattern.Length || pattern[groupStart + 1] != (byte)'?')
        {
            return false;
        }

        if (groupStart + 2 < pattern.Length && pattern[groupStart + 2] == (byte)':')
        {
            contentStart = groupStart + 3;
            return true;
        }

        if (TryReadRegexNamedCapturePrefix(pattern, groupStart + 2, out contentStart))
        {
            return true;
        }

        bool sawFlag = false;
        for (int index = groupStart + 2; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'-')
            {
                continue;
            }

            if (IsInlineRegexFlag(value))
            {
                sawFlag = true;
                continue;
            }

            if (value == (byte)':')
            {
                contentStart = index + 1;
                return sawFlag;
            }

            if (value == (byte)')')
            {
                contentStart = index + 1;
                return sawFlag;
            }

            return false;
        }

        return false;
    }

    private static bool TryReadRegexNamedCapturePrefix(ReadOnlySpan<byte> pattern, int prefixStart, out int contentStart)
    {
        contentStart = prefixStart;
        int nameStart;
        if (prefixStart + 1 < pattern.Length && pattern[prefixStart] == (byte)'P' && pattern[prefixStart + 1] == (byte)'<')
        {
            nameStart = prefixStart + 2;
        }
        else if (prefixStart < pattern.Length && pattern[prefixStart] == (byte)'<')
        {
            nameStart = prefixStart + 1;
        }
        else
        {
            return false;
        }

        for (int index = nameStart; index < pattern.Length; index++)
        {
            if (pattern[index] == (byte)'>')
            {
                contentStart = index + 1;
                return true;
            }
        }

        return false;
    }

    private static int SkipRegexRepetition(ReadOnlySpan<byte> pattern, int repetitionStart)
    {
        int next = repetitionStart + 1;
        if (pattern[repetitionStart] == (byte)'{')
        {
            for (int index = repetitionStart + 1; index < pattern.Length; index++)
            {
                if (pattern[index] == (byte)'}')
                {
                    next = index + 1;
                    break;
                }
            }
        }

        if (next < pattern.Length && pattern[next] == (byte)'?')
        {
            next++;
        }

        return next - 1;
    }

    private static string BuildRegexParseError(ReadOnlySpan<byte> pattern, int offset, string error)
    {
        string displayPattern = "(?:" + BuildRegexErrorPatternDisplay(pattern) + ")";
        string caret = new string(' ', 4 + 3 + Math.Max(offset, 0)) + "^";
        return "regex parse error:\n    " + displayPattern + "\n" + caret + "\nerror: " + error;
    }

    private static string BuildRegexErrorPatternDisplay(ReadOnlySpan<byte> pattern)
    {
        var builder = new StringBuilder(pattern.Length);
        for (int index = 0; index < pattern.Length; index++)
        {
            byte value = pattern[index];
            if (value == (byte)'\t')
            {
                builder.Append(@"\t");
            }
            else if (value == (byte)'\r')
            {
                builder.Append(@"\r");
            }
            else
            {
                builder.Append(value is >= 0x20 and <= 0x7e ? (char)value : '\uFFFD');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Resolves whether ASCII case-insensitive matching is active for a pattern set.
    /// </summary>
    /// <param name="pattern">The patterns to inspect.</param>
    /// <param name="caseMode">The requested case mode.</param>
    /// <returns><see langword="true" /> when ASCII matching should ignore case.</returns>
    public static bool IsAsciiCaseInsensitive(IReadOnlyList<byte[]> pattern, CliCaseMode caseMode)
    {
        return caseMode == CliCaseMode.Insensitive
            || (caseMode == CliCaseMode.Smart && !ContainsAsciiUppercase(pattern));
    }

    private static bool ContainsAsciiUppercase(byte[] bytes)
    {
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] is >= (byte)'A' and <= (byte)'Z')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAsciiUppercase(IReadOnlyList<byte[]> patterns)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            if (ContainsAsciiUppercase(patterns[index]))
            {
                return true;
            }
        }

        return false;
    }

}
