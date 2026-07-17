namespace Scout;

/// <summary>
/// Verifies operation-scoped native regex-plan selection and ownership.
/// </summary>
public sealed class NativeRegexSearchPlanFactoryTests
{
    /// <summary>
    /// Verifies CRLF multiline searches that remain record-oriented preserve the carriage return.
    /// </summary>
    [Fact]
    public void CreatesPreservedCrlfLinePlan()
    {
        CliLowArgs lowArgs = ParseLowArgs("-U", "--crlf", @"\r");

        RegexSearchPlan plan = Assert.IsType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create([@"\r"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.False(plan.Options.Multiline);
        Assert.True(plan.Options.Crlf);
        Assert.True(plan.Options.PreserveCrlfCarriageReturn);
    }

    /// <summary>
    /// Verifies standard multiline syntax that can consume a record terminator selects whole-buffer matching.
    /// </summary>
    [Fact]
    public void CreatesStandardWholeBufferPlanWhenRequired()
    {
        CliLowArgs lowArgs = ParseLowArgs("-U", "pattern");

        RegexSearchPlan plan = Assert.IsType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create(["a\nb"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.True(plan.Options.Multiline);
        Assert.False(plan.Options.PreserveCrlfCarriageReturn);
    }

    /// <summary>
    /// Verifies JSON line anchors select whole-buffer matching so their record positions remain authoritative.
    /// </summary>
    [Fact]
    public void CreatesJsonWholeBufferPlanForLineAnchors()
    {
        CliLowArgs lowArgs = ParseLowArgs("--json", "-U", "pattern");

        RegexSearchPlan plan = Assert.IsType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create(["^needle$"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.True(plan.Options.Multiline);
    }

    /// <summary>
    /// Verifies NUL-delimited JSON context uses the record plan required by its context renderer.
    /// </summary>
    [Fact]
    public void CreatesJsonNullDataLinePlanForContext()
    {
        CliLowArgs lowArgs = ParseLowArgs("--json", "-U", "--null-data", "-C1", "pattern");

        RegexSearchPlan plan = Assert.IsType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create(["needle"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.False(plan.Options.Multiline);
        Assert.True(plan.Options.NullData);
    }

    /// <summary>
    /// Verifies NUL-delimited JSON without context searches the complete input with one matcher.
    /// </summary>
    [Fact]
    public void CreatesJsonNullDataWholeBufferPlanWithoutContext()
    {
        CliLowArgs lowArgs = ParseLowArgs("--json", "-U", "--null-data", "pattern");

        RegexSearchPlan plan = Assert.IsType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create(["needle"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.True(plan.Options.Multiline);
        Assert.True(plan.Options.NullData);
    }

    /// <summary>
    /// Verifies the operation-scoped plan applies the CLI DFA cache budget to native compilation.
    /// </summary>
    [Fact]
    public void AppliesDfaSizeLimitToAuthoritativeCompilation()
    {
        CliLowArgs defaultArgs = ParseLowArgs("pattern");
        CliLowArgs constrainedArgs = ParseLowArgs("--dfa-size-limit=0", "pattern");
        byte[][] patterns = [@"(?-u:\w{5}\s+\w{5}\s+\w{5})"u8.ToArray()];

        RegexSearchPlan defaultPlan = NativeRegexSearchPlanFactory.Create(
            patterns,
            defaultArgs,
            asciiCaseInsensitive: false);
        RegexSearchPlan constrainedPlan = NativeRegexSearchPlanFactory.Create(
            patterns,
            constrainedArgs,
            asciiCaseInsensitive: false);

        Assert.Equal(RegexEngineKind.SparseDfa, defaultPlan.Matcher.EngineKind);
        Assert.Equal(RegexEngineKind.PikeVm, constrainedPlan.Matcher.EngineKind);
        Assert.Equal(
            defaultPlan.Matcher.Find("prefix alpha bravo charl suffix"u8),
            constrainedPlan.Matcher.Find("prefix alpha bravo charl suffix"u8));
    }

    /// <summary>
    /// Verifies standard multiline scope follows the effective syntax of the combined parsed expression.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="arguments">The command-line options that establish the root regex flags.</param>
    /// <param name="expectedWholeBuffer">Whether the parsed expression requires whole-buffer execution.</param>
    [Theory]
    [InlineData("literal", "-U", false)]
    [InlineData(@"\S", "-U", false)]
    [InlineData(".", "-U", false)]
    [InlineData(".", "-U --multiline-dotall", true)]
    [InlineData("(?s:.)", "-U", true)]
    [InlineData("(?s)(?-s:.)", "-U", false)]
    [InlineData(@"\n", "-U", true)]
    [InlineData(@"[^a]", "-U", true)]
    [InlineData(@"\p{Control}", "-U", true)]
    [InlineData(@"\Aabsolute", "-U", true)]
    [InlineData("(?-m:^absolute)", "-U", true)]
    public void CreatesStandardScopeFromParsedSyntax(
        string pattern,
        string arguments,
        bool expectedWholeBuffer)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(arguments);
        CliLowArgs lowArgs = ParseLowArgs(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        RegexSearchPlan plan = NativeRegexSearchPlanFactory.Create(
            [System.Text.Encoding.UTF8.GetBytes(pattern)],
            lowArgs,
            asciiCaseInsensitive: false);

        Assert.Equal(expectedWholeBuffer, plan.Options.Multiline);
    }

    /// <summary>
    /// Verifies parsed syntax records atoms that consume only NUL for binary-mode diagnostics.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="expected">Whether the parsed expression explicitly consumes NUL.</param>
    [Theory]
    [InlineData(@"\x00", true)]
    [InlineData(@"\x{0}", true)]
    [InlineData(@"[\x00]", true)]
    [InlineData(@"(?:prefix\x00)?", true)]
    [InlineData(".", false)]
    [InlineData(@"[\x00-\x01]", false)]
    [InlineData(@"\W", false)]
    public void RecordsExplicitNulFromParsedSyntax(string pattern, bool expected)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        CliLowArgs lowArgs = ParseLowArgs("pattern");

        RegexSearchPlan plan = NativeRegexSearchPlanFactory.Create(
            [System.Text.Encoding.UTF8.GetBytes(pattern)],
            lowArgs,
            asciiCaseInsensitive: false);

        Assert.Equal(expected, plan.ContainsExplicitNul);
    }

    /// <summary>
    /// Verifies record-oriented plans report parsed expressions that explicitly consume their record terminator.
    /// </summary>
    /// <param name="arguments">The command-line options that select the record terminator.</param>
    /// <param name="pattern">The regex pattern.</param>
    [Theory]
    [InlineData("pattern", @"\n")]
    [InlineData("--null-data pattern", @"\x00")]
    public void RejectsExplicitRecordTerminatorFromParsedSyntax(
        string arguments,
        string pattern)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(pattern);
        CliLowArgs lowArgs = ParseLowArgs(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Throws<RegexLineTerminatorException>(() => NativeRegexSearchPlanFactory.Create(
            [System.Text.Encoding.UTF8.GetBytes(pattern)],
            lowArgs,
            asciiCaseInsensitive: false));
    }

    /// <summary>
    /// Verifies every native dispatch and rendering layer reuses the operation-scoped plan.
    /// </summary>
    [Fact]
    public void CliDispatchCompilesOnlyAtOperationBoundary()
    {
        string root = FindRepositoryRoot();
        string application = File.ReadAllText(Path.Combine(root, "src", "Scout.App", "ScoutApplication.cs"));
        string[] downstreamFiles =
        [
            "StandardSearchOperations.cs",
            "StandardSearchByteOperations.cs",
            "StandardSearchTargetOperations.cs",
            "LargeFileSearchOperations.cs",
            "ContextSearchOperations.cs",
            "MultilineSearchOperations.cs",
            "JsonSearchOperations.cs",
        ];

        Assert.Equal(1, CountOccurrences(application, "NativeRegexSearchPlanFactory.Create("));
        for (int index = 0; index < downstreamFiles.Length; index++)
        {
            string source = File.ReadAllText(Path.Combine(root, "src", "Scout.App", downstreamFiles[index]));
            Assert.DoesNotContain("RegexSearchPlan.Create(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateMultilinePlan(", source, StringComparison.Ordinal);
        }
    }

    private static CliLowArgs ParseLowArgs(params string[] arguments)
    {
        var osArguments = new OsString[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index] = OsString.FromUnixBytes(System.Text.Encoding.UTF8.GetBytes(arguments[index]));
        }

        CliParseResult result = CliParser.Parse(osArguments);
        Assert.Equal(CliParseStatus.Ok, result.Status);
        return Assert.IsType<CliLowArgs>(result.LowArgs);
    }

    private static int CountOccurrences(string value, string search)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }
}
