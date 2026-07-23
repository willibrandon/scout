namespace Scout;

/// <summary>
/// Compiles and retains the regex engine selected for one search operation.
/// </summary>
internal static class RegexEnginePlanner
{
    private const string ErrorDivider = "~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~";
    private const string SyntaxPatternDataKey = "Scout.RegexSyntaxPattern";
    private const string SyntaxByteOffsetDataKey = "Scout.RegexSyntaxByteOffset";
    private const string SyntaxErrorDataKey = "Scout.RegexSyntaxError";

    /// <summary>
    /// Compiles the requested engine and retains the successful matcher for dispatch.
    /// </summary>
    /// <param name="patterns">The raw ordered pattern set.</param>
    /// <param name="lowArgs">The parsed low-level command-line arguments.</param>
    /// <param name="plan">Receives the selected compiled engine plan.</param>
    /// <param name="error">Receives the engine construction error.</param>
    /// <returns><see langword="true" /> when an engine was compiled successfully.</returns>
    internal static bool TryCreate(
        IReadOnlyList<byte[]> patterns,
        CliLowArgs lowArgs,
        out RegexEnginePlan? plan,
        out ScoutError? error)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(lowArgs);

        plan = null;
        error = null;
        if (lowArgs.RegexEngine == CliRegexEngine.Pcre2)
        {
            Pcre2SearchPlan? pcre2Plan = null;
            try
            {
                if (!TryCreatePcre2(patterns, lowArgs, out pcre2Plan, out error))
                {
                    return false;
                }

                plan = new RegexEnginePlan(
                    pcre2Plan!.Patterns,
                    nativePlan: null,
                    pcre2Plan,
                    asciiCaseInsensitive: false);
                pcre2Plan = null;
                return true;
            }
            finally
            {
                pcre2Plan?.Dispose();
            }
        }

        if (TryCreateNative(
            patterns,
            lowArgs,
            out List<byte[]>? nativePatterns,
            out RegexSearchPlan? nativePlan,
            out bool asciiCaseInsensitive,
            out ScoutError? nativeError))
        {
            plan = new RegexEnginePlan(
                nativePatterns!,
                nativePlan,
                pcre2Plan: null,
                asciiCaseInsensitive);
            return true;
        }

        if (lowArgs.RegexEngine == CliRegexEngine.Default)
        {
            error = nativeError;
            return false;
        }

        Pcre2SearchPlan? fallbackPlan = null;
        try
        {
            if (TryCreatePcre2(patterns, lowArgs, out fallbackPlan, out ScoutError? pcre2Error))
            {
                plan = new RegexEnginePlan(
                    fallbackPlan!.Patterns,
                    nativePlan: null,
                    fallbackPlan,
                    asciiCaseInsensitive: false);
                fallbackPlan = null;
                return true;
            }

            error = BuildCombinedError(nativeError!, pcre2Error!);
            return false;
        }
        finally
        {
            fallbackPlan?.Dispose();
        }
    }

    private static bool TryCreateNative(
        IReadOnlyList<byte[]> patterns,
        CliLowArgs lowArgs,
        out List<byte[]>? preparedPatterns,
        out RegexSearchPlan? plan,
        out bool asciiCaseInsensitive,
        out ScoutError? error)
    {
        preparedPatterns = ClonePatterns(patterns);
        plan = null;
        asciiCaseInsensitive = false;
        error = null;

        if (lowArgs.FixedStrings)
        {
            PatternPreparation.EscapeFixedStringPatterns(preparedPatterns);
        }
        else
        {
            if (!PatternPreparation.TryValidateRegexRepetitionExpressions(preparedPatterns, out error))
            {
                return false;
            }

            PatternPreparation.WrapRegexPatterns(preparedPatterns);
        }

        if (!PatternPreparation.TryValidateRegexSizeLimit(preparedPatterns, lowArgs, out error))
        {
            return false;
        }

        asciiCaseInsensitive = PatternPreparation.IsAsciiCaseInsensitive(preparedPatterns, lowArgs.CaseMode);
        if (!lowArgs.Unicode && asciiCaseInsensitive)
        {
            PatternPreparation.WrapNonAsciiPatterns(preparedPatterns);
        }

        if (!lowArgs.FixedStrings && !lowArgs.Unicode)
        {
            PatternPreparation.WrapNoUnicodePatterns(preparedPatterns);
        }

        try
        {
            plan = NativeRegexSearchPlanFactory.Create(preparedPatterns, lowArgs, asciiCaseInsensitive);
        }
        catch (RegexLineTerminatorException)
        {
            error = new ScoutError(PatternPreparation.BuildLineTerminatorPatternError(lowArgs.NullData));
            return false;
        }
        catch (FormatException exception) when (TryGetSyntaxDiagnostic(
            exception,
            out byte[]? pattern,
            out int byteOffset,
            out string? parseError))
        {
            error = new ScoutError(PatternPreparation.BuildRegexParseError(
                pattern!,
                byteOffset,
                parseError!,
                wrapPattern: false));
            return false;
        }
        catch (FormatException exception)
        {
            error = new ScoutError(exception.Message);
            return false;
        }

        if (!lowArgs.TextMode &&
            !lowArgs.NullData &&
            !lowArgs.FixedStrings &&
            plan.ContainsExplicitNul)
        {
            error = new ScoutError(
                "pattern contains \"\\0\" but it is impossible to match\n\n" +
                "Consider enabling text mode with the --text flag (or -a for short). Otherwise,\n" +
                "binary detection is enabled and matching a NUL byte is impossible.");
            plan = null;
            return false;
        }

        return true;
    }

    private static bool TryCreatePcre2(
        IReadOnlyList<byte[]> patterns,
        CliLowArgs lowArgs,
        out Pcre2SearchPlan? plan,
        out ScoutError? error)
    {
        plan = null;
        error = null;
        if (!Pcre2Library.IsAvailable)
        {
            error = new ScoutError(Pcre2Library.UnavailableErrorMessage);
            return false;
        }

        if (!Pcre2SearchOperations.CanRun(lowArgs))
        {
            error = new ScoutError("PCRE2 search does not support this option combination");
            return false;
        }

        List<byte[]> preparedPatterns = ClonePatterns(patterns);
        if (lowArgs.FixedStrings)
        {
            PatternPreparation.EscapeFixedStringPatterns(preparedPatterns);
        }

        byte[] pattern = BuildPcre2Pattern(preparedPatterns);
        Pcre2CompileOptions compileOptions = Pcre2SearchOperations.GetPcre2CompileOptions(
            lowArgs,
            preparedPatterns);
        try
        {
            plan = new Pcre2SearchPlan(preparedPatterns, pattern, compileOptions);
            return true;
        }
        catch (Pcre2Exception exception)
        {
            error = new ScoutError(exception.Message);
            return false;
        }
    }

    private static List<byte[]> ClonePatterns(IReadOnlyList<byte[]> patterns)
    {
        var cloned = new List<byte[]>(patterns.Count);
        for (int index = 0; index < patterns.Count; index++)
        {
            cloned.Add((byte[])patterns[index].Clone());
        }

        return cloned;
    }

    private static bool TryGetSyntaxDiagnostic(
        FormatException exception,
        out byte[]? pattern,
        out int byteOffset,
        out string? parseError)
    {
        pattern = exception.Data[SyntaxPatternDataKey] as byte[];
        byteOffset = exception.Data[SyntaxByteOffsetDataKey] is int offset ? offset : -1;
        parseError = exception.Data[SyntaxErrorDataKey] as string;
        return pattern is not null && byteOffset >= 0 && !string.IsNullOrEmpty(parseError);
    }

    private static byte[] BuildPcre2Pattern(List<byte[]> patterns)
    {
        if (patterns.Count == 1)
        {
            return patterns[0];
        }

        using MemoryStream buffer = new();
        for (int index = 0; index < patterns.Count; index++)
        {
            if (index > 0)
            {
                buffer.WriteByte((byte)'|');
            }

            buffer.Write("(?:"u8);
            buffer.Write(patterns[index]);
            buffer.WriteByte((byte)')');
        }

        return buffer.ToArray();
    }

    private static ScoutError BuildCombinedError(ScoutError nativeError, ScoutError pcre2Error)
    {
        return new ScoutError(
            "regex could not be compiled with either the default regex engine or with PCRE2.\n\n" +
            "default regex engine error:\n" +
            ErrorDivider + "\n" +
            nativeError.FormatAlternate() + "\n" +
            ErrorDivider + "\n\n" +
            "PCRE2 regex engine error:\n" +
            pcre2Error.FormatAlternate());
    }
}
