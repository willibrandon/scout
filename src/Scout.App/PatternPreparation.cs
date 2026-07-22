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
        if (TryValidateRegexRepetitionExpressions(patterns, out ScoutError? error))
        {
            return true;
        }

        diagnostics.ErrorMessage(error!.WithContext(ScoutErrorContext.ProgramContext()));
        return false;
    }

    /// <summary>
    /// Validates that repetition operators have preceding expressions without emitting a diagnostic.
    /// </summary>
    /// <param name="patterns">The patterns to validate.</param>
    /// <param name="error">Receives the parse error.</param>
    /// <returns><see langword="true" /> when every repetition is valid.</returns>
    internal static bool TryValidateRegexRepetitionExpressions(
        IReadOnlyList<byte[]> patterns,
        out ScoutError? error)
    {
        error = null;
        for (int index = 0; index < patterns.Count; index++)
        {
            if (TryFindMissingRepetitionExpression(patterns[index], out int offset))
            {
                error = new ScoutError(BuildRegexParseError(
                    patterns[index],
                    offset,
                    "repetition operator missing expression"));
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
        if (TryValidateRegexSizeLimit(patterns, lowArgs, out ScoutError? error))
        {
            return true;
        }

        diagnostics.ErrorMessage(error!.WithContext(ScoutErrorContext.ProgramContext()));
        return false;
    }

    /// <summary>
    /// Validates the configured compiled-regex size limit without emitting a diagnostic.
    /// </summary>
    /// <param name="patterns">The patterns to estimate.</param>
    /// <param name="lowArgs">The parsed low-level arguments.</param>
    /// <param name="error">Receives the validation error.</param>
    /// <returns><see langword="true" /> when the estimated compiled size is within the limit.</returns>
    internal static bool TryValidateRegexSizeLimit(
        List<byte[]> patterns,
        CliLowArgs lowArgs,
        out ScoutError? error)
    {
        error = null;
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
                error = new ScoutError($"compiled regex exceeds size limit of {limit}");
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

    private static bool IsInlineRegexFlag(byte value)
    {
        return value is (byte)'i' or (byte)'m' or (byte)'s' or (byte)'U' or (byte)'u' or (byte)'x' or (byte)'R';
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

    internal static string BuildRegexParseError(
        ReadOnlySpan<byte> pattern,
        int offset,
        string error,
        bool wrapPattern = true)
    {
        string patternDisplay = BuildRegexErrorPatternDisplay(pattern);
        string displayPattern = wrapPattern ? "(?:" + patternDisplay + ")" : patternDisplay;
        int displayOffset = Math.Max(offset, 0) + (wrapPattern ? 3 : 0);
        string caret = new string(' ', 4 + displayOffset) + "^";
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
