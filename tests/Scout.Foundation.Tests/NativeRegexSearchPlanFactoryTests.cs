namespace Scout;

/// <summary>
/// Verifies operation-scoped native regex-plan selection and ownership.
/// </summary>
[TestClass]
public sealed class NativeRegexSearchPlanFactoryTests
{
    /// <summary>
    /// Verifies CRLF multiline searches that remain record-oriented preserve the carriage return.
    /// </summary>
    [TestMethod]
    public void CreatesPreservedCrlfLinePlan()
    {
        CliLowArgs lowArgs = ParseLowArgs("-U", "--crlf", @"\r");

        RegexSearchPlan plan = Assert.IsExactInstanceOfType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create([@"\r"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.IsFalse(plan.Options.Multiline);
        Assert.IsTrue(plan.Options.Crlf);
        Assert.IsTrue(plan.Options.PreserveCrlfCarriageReturn);
    }

    /// <summary>
    /// Verifies standard multiline syntax that can consume a record terminator selects whole-buffer matching.
    /// </summary>
    [TestMethod]
    public void CreatesStandardWholeBufferPlanWhenRequired()
    {
        CliLowArgs lowArgs = ParseLowArgs("-U", "pattern");

        RegexSearchPlan plan = Assert.IsExactInstanceOfType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create(["a\nb"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.IsTrue(plan.Options.Multiline);
        Assert.IsFalse(plan.Options.PreserveCrlfCarriageReturn);
    }

    /// <summary>
    /// Verifies JSON line anchors select whole-buffer matching so their record positions remain authoritative.
    /// </summary>
    [TestMethod]
    public void CreatesJsonWholeBufferPlanForLineAnchors()
    {
        CliLowArgs lowArgs = ParseLowArgs("--json", "-U", "pattern");

        RegexSearchPlan plan = Assert.IsExactInstanceOfType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create(["^needle$"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.IsTrue(plan.Options.Multiline);
    }

    /// <summary>
    /// Verifies NUL-delimited JSON context uses the record plan required by its context renderer.
    /// </summary>
    [TestMethod]
    public void CreatesJsonNullDataLinePlanForContext()
    {
        CliLowArgs lowArgs = ParseLowArgs("--json", "-U", "--null-data", "-C1", "pattern");

        RegexSearchPlan plan = Assert.IsExactInstanceOfType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create(["needle"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.IsFalse(plan.Options.Multiline);
        Assert.IsTrue(plan.Options.NullData);
    }

    /// <summary>
    /// Verifies NUL-delimited JSON without context searches the complete input with one matcher.
    /// </summary>
    [TestMethod]
    public void CreatesJsonNullDataWholeBufferPlanWithoutContext()
    {
        CliLowArgs lowArgs = ParseLowArgs("--json", "-U", "--null-data", "pattern");

        RegexSearchPlan plan = Assert.IsExactInstanceOfType<RegexSearchPlan>(
            NativeRegexSearchPlanFactory.Create(["needle"u8.ToArray()], lowArgs, asciiCaseInsensitive: false));

        Assert.IsTrue(plan.Options.Multiline);
        Assert.IsTrue(plan.Options.NullData);
    }

    /// <summary>
    /// Verifies the operation-scoped plan applies the CLI DFA cache budget to native compilation.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(RegexEngineKind.SparseDfa, defaultPlan.Matcher.EngineKind);
        Assert.AreEqual(RegexEngineKind.PikeVm, constrainedPlan.Matcher.EngineKind);
        Assert.AreEqual(
            defaultPlan.Matcher.Find("prefix alpha bravo charl suffix"u8),
            constrainedPlan.Matcher.Find("prefix alpha bravo charl suffix"u8));
    }

    /// <summary>
    /// Verifies standard multiline scope follows the effective syntax of the combined parsed expression.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="arguments">The command-line options that establish the root regex flags.</param>
    /// <param name="expectedWholeBuffer">Whether the parsed expression requires whole-buffer execution.</param>
    [TestMethod]
    [DataRow("literal", "-U", false)]
    [DataRow(@"\S", "-U", false)]
    [DataRow(".", "-U", false)]
    [DataRow(".", "-U --multiline-dotall", true)]
    [DataRow("(?s:.)", "-U", true)]
    [DataRow("(?s)(?-s:.)", "-U", false)]
    [DataRow(@"\n", "-U", true)]
    [DataRow(@"[^a]", "-U", true)]
    [DataRow(@"\p{Control}", "-U", true)]
    [DataRow(@"\Aabsolute", "-U", true)]
    [DataRow("(?-m:^absolute)", "-U", true)]
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

        Assert.AreEqual(expectedWholeBuffer, plan.Options.Multiline);
    }

    /// <summary>
    /// Verifies parsed syntax records atoms that consume only NUL for binary-mode diagnostics.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="expected">Whether the parsed expression explicitly consumes NUL.</param>
    [TestMethod]
    [DataRow(@"\x00", true)]
    [DataRow(@"\x{0}", true)]
    [DataRow(@"[\x00]", true)]
    [DataRow(@"(?:prefix\x00)?", true)]
    [DataRow(".", false)]
    [DataRow(@"[\x00-\x01]", false)]
    [DataRow(@"\W", false)]
    public void RecordsExplicitNulFromParsedSyntax(string pattern, bool expected)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        CliLowArgs lowArgs = ParseLowArgs("pattern");

        RegexSearchPlan plan = NativeRegexSearchPlanFactory.Create(
            [System.Text.Encoding.UTF8.GetBytes(pattern)],
            lowArgs,
            asciiCaseInsensitive: false);

        Assert.AreEqual(expected, plan.ContainsExplicitNul);
    }

    /// <summary>
    /// Verifies record-oriented plans report parsed expressions that explicitly consume their record terminator.
    /// </summary>
    /// <param name="arguments">The command-line options that select the record terminator.</param>
    /// <param name="pattern">The regex pattern.</param>
    [TestMethod]
    [DataRow("pattern", @"\n")]
    [DataRow("--null-data pattern", @"\x00")]
    public void RejectsExplicitRecordTerminatorFromParsedSyntax(
        string arguments,
        string pattern)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(pattern);
        CliLowArgs lowArgs = ParseLowArgs(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.ThrowsExactly<RegexLineTerminatorException>(() => NativeRegexSearchPlanFactory.Create(
            [System.Text.Encoding.UTF8.GetBytes(pattern)],
            lowArgs,
            asciiCaseInsensitive: false));
    }

    /// <summary>
    /// Verifies every native dispatch and rendering layer reuses the operation-scoped plan.
    /// </summary>
    [TestMethod]
    public void CliDispatchCompilesOnlyAtOperationBoundary()
    {
        string root = FindRepositoryRoot();
        string application = File.ReadAllText(Path.Join(root, "src", "Scout.App", "ScoutApplication.cs"));
        string enginePlanner = File.ReadAllText(Path.Join(root, "src", "Scout.App", "RegexEnginePlanner.cs"));
        string pcre2Operations = File.ReadAllText(Path.Join(root, "src", "Scout.App", "Pcre2SearchOperations.cs"));
        string workerRegexes = File.ReadAllText(Path.Join(root, "src", "Scout.App", "Pcre2WorkerRegexes.cs"));
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

        Assert.AreEqual(0, CountOccurrences(application, "NativeRegexSearchPlanFactory.Create("));
        Assert.AreEqual(1, CountOccurrences(enginePlanner, "NativeRegexSearchPlanFactory.Create("));
        Assert.DoesNotContain("ShouldAutoUse", pcre2Operations, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultRegexCompileFails", pcre2Operations, StringComparison.Ordinal);
        Assert.Contains("plan.Regex", pcre2Operations, StringComparison.Ordinal);
        Assert.Contains("new Pcre2WorkerRegexes(regex, pcre2Pattern, compileOptions)", pcre2Operations, StringComparison.Ordinal);
        Assert.Contains("Interlocked.CompareExchange(ref borrowedClaimed, 1, 0) == 0", workerRegexes, StringComparison.Ordinal);
        for (int index = 0; index < downstreamFiles.Length; index++)
        {
            string source = File.ReadAllText(Path.Join(root, "src", "Scout.App", downstreamFiles[index]));
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
        Assert.AreEqual(CliParseStatus.Ok, result.Status);
        return Assert.IsExactInstanceOfType<CliLowArgs>(result.LowArgs);
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
            if (File.Exists(Path.Join(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }
}
