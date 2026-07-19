namespace Scout;

/// <summary>
/// Verifies operation-scoped regex engine selection and matcher ownership.
/// </summary>
public sealed class RegexEnginePlannerTests
{
    /// <summary>
    /// Verifies explicit default selection retains the authoritative native matcher and its options.
    /// </summary>
    [Fact]
    public void DefaultEngineRetainsAuthoritativeNativePlan()
    {
        CliLowArgs lowArgs = ParseLowArgs("--engine=default", "--word-regexp", "needle");
        RegexEnginePlan? plan = null;
        try
        {
            bool created = RegexEnginePlanner.TryCreate(
                ["needle"u8.ToArray()],
                lowArgs,
                out plan,
                out ScoutError? error);

            Assert.True(created, error?.FormatAlternate());
            RegexEnginePlan selectedPlan = Assert.IsType<RegexEnginePlan>(plan);
            Assert.False(selectedPlan.UsesPcre2);
            RegexSearchPlan nativePlan = Assert.IsType<RegexSearchPlan>(selectedPlan.NativePlan);
            Assert.True(nativePlan.Options.WordRegexp);
            Assert.Equal(new RegexMatch(1, 6), nativePlan.Matcher.Find(" needle "u8));
        }
        finally
        {
            plan?.Dispose();
        }
    }

    /// <summary>
    /// Verifies auto selection retains the native plan when authoritative construction succeeds.
    /// </summary>
    [Fact]
    public void AutoEngineRetainsSuccessfulNativePlan()
    {
        CliLowArgs lowArgs = ParseLowArgs("--engine=auto", "--fixed-strings", "a.c");
        byte[] rawPattern = "a.c"u8.ToArray();
        RegexEnginePlan? plan = null;
        try
        {
            bool created = RegexEnginePlanner.TryCreate(
                [rawPattern],
                lowArgs,
                out plan,
                out ScoutError? error);

            Assert.True(created, error?.FormatAlternate());
            RegexEnginePlan selectedPlan = Assert.IsType<RegexEnginePlan>(plan);
            Assert.False(selectedPlan.UsesPcre2);
            Assert.NotNull(selectedPlan.NativePlan);
            Assert.Equal("a\\.c"u8.ToArray(), Assert.Single(selectedPlan.Patterns));
            Assert.Equal("a.c"u8.ToArray(), rawPattern);
        }
        finally
        {
            plan?.Dispose();
        }
    }

    /// <summary>
    /// Verifies auto selection reports both construction errors when neither engine is available.
    /// </summary>
    [Fact]
    public void AutoEngineReportsBothConstructionErrors()
    {
        CliLowArgs lowArgs = ParseLowArgs("--engine=auto", "(?=a)a");

        ScoutError error = GetPlanningError(["(?=a)a"u8.ToArray()], lowArgs);

        Assert.StartsWith(
            "regex could not be compiled with either the default regex engine or with PCRE2.",
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains("default regex engine error:\n", error.Message, StringComparison.Ordinal);
        Assert.Contains(new string('~', 79), error.Message, StringComparison.Ordinal);
        Assert.Contains("PCRE2 regex engine error:\n", error.Message, StringComparison.Ordinal);
        Assert.Contains(Pcre2Library.UnavailableErrorMessage, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies PCRE2-capable escapes rejected by the native parser reach the PCRE2 construction attempt.
    /// </summary>
    /// <param name="pattern">The PCRE2-capable pattern rejected by the native engine.</param>
    [Theory]
    [InlineData(@"(Scout)\1")]
    [InlineData(@"(?<word>Scout)\k<word>")]
    [InlineData(@"Scout\KRegex")]
    [InlineData(@"\X")]
    public void AutoEngineRoutesUnsupportedEscapesToPcre2Attempt(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        CliLowArgs lowArgs = ParseLowArgs("--engine=auto", pattern);

        ScoutError error = GetPlanningError(
            [System.Text.Encoding.UTF8.GetBytes(pattern)],
            lowArgs);

        Assert.Contains("default regex engine error:\n", error.Message, StringComparison.Ordinal);
        Assert.Contains("PCRE2 regex engine error:\n", error.Message, StringComparison.Ordinal);
        Assert.Contains(Pcre2Library.UnavailableErrorMessage, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies explicit default selection returns only the native construction error.
    /// </summary>
    [Fact]
    public void DefaultEngineReturnsNativeConstructionError()
    {
        CliLowArgs lowArgs = ParseLowArgs("--engine=default", "(?=a)a");

        ScoutError error = GetPlanningError(["(?=a)a"u8.ToArray()], lowArgs);

        Assert.DoesNotContain("PCRE2 regex engine error", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("either the default regex engine", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies operation-scoped planning preserves ripgrep-compatible repetition diagnostics.
    /// </summary>
    [Fact]
    public void DefaultEnginePreservesRepetitionParseDiagnostic()
    {
        CliLowArgs lowArgs = ParseLowArgs("--engine=default", "*");

        ScoutError error = GetPlanningError(["*"u8.ToArray()], lowArgs);

        Assert.Equal(
            "regex parse error:\n    (?:*)\n       ^\n" +
            "error: repetition operator missing expression",
            error.Message);
    }

    /// <summary>
    /// Verifies explicit PCRE2 selection reports the linked-runtime diagnostic directly.
    /// </summary>
    [Fact]
    public void Pcre2EngineReportsUnavailableRuntimeDirectly()
    {
        CliLowArgs lowArgs = ParseLowArgs("--engine=pcre2", "needle");

        ScoutError error = GetPlanningError(["needle"u8.ToArray()], lowArgs);

        Assert.Equal(Pcre2Library.UnavailableErrorMessage, error.Message);
    }

    /// <summary>
    /// Verifies native size-policy failure participates in auto fallback selection.
    /// </summary>
    [Fact]
    public void AutoEngineFallsBackAfterNativeSizePolicyFailure()
    {
        CliLowArgs lowArgs = ParseLowArgs(
            "--engine=auto",
            "--regex-size-limit=1",
            "needle");

        ScoutError error = GetPlanningError(["needle"u8.ToArray()], lowArgs);

        Assert.Contains("compiled regex exceeds size limit of 1", error.Message, StringComparison.Ordinal);
        Assert.Contains(Pcre2Library.UnavailableErrorMessage, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies native binary NUL policy remains part of authoritative engine construction.
    /// </summary>
    [Fact]
    public void DefaultEnginePreservesBinaryNulPolicy()
    {
        CliLowArgs lowArgs = ParseLowArgs("--engine=default", @"\x00");

        ScoutError error = GetPlanningError([@"\x00"u8.ToArray()], lowArgs);

        Assert.StartsWith("pattern contains \"\\0\"", error.Message, StringComparison.Ordinal);
    }

    private static ScoutError GetPlanningError(
        IReadOnlyList<byte[]> patterns,
        CliLowArgs lowArgs)
    {
        RegexEnginePlan? plan = null;
        try
        {
            bool created = RegexEnginePlanner.TryCreate(patterns, lowArgs, out plan, out ScoutError? error);
            Assert.False(created);
            return Assert.IsType<ScoutError>(error);
        }
        finally
        {
            plan?.Dispose();
        }
    }

    private static CliLowArgs ParseLowArgs(params string[] arguments)
    {
        var osArguments = new OsString[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index] = OsString.FromText(arguments[index]);
        }

        CliParseResult result = CliParser.Parse(osArguments);
        Assert.Equal(CliParseStatus.Ok, result.Status);
        return result.LowArgs!;
    }
}
