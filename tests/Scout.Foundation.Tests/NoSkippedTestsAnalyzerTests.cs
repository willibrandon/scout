using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

/// <summary>
/// Verifies Scout's no-skipped-tests analyzer behavior.
/// </summary>
[TestClass]
public sealed class NoSkippedTestsAnalyzerTests
{
    /// <summary>
    /// Verifies a normal test attribute is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsNormalTestsAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            """
            namespace Scout;

            public sealed class SampleTests
            {
                [TestMethod]
                public void Passes()
                {
                }
            }
            """).ConfigureAwait(true);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies ignored test attributes are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsIgnoredTestAttributesAsync()
    {
        string ignore = "Ig" + "nore";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [TestMethod]
                [{{ignore}}("reason")]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, ignore);
    }

    /// <summary>
    /// Verifies generated test sources are rejected even when their hint path is outside the tests folder.
    /// </summary>
    [TestMethod]
    public async Task ReportsGeneratedIgnoredTestAttributesInTestProjectsAsync()
    {
        string ignore = "Ig" + "nore";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAtPathAsync(
            "PortedRgGeneratedCase.g.cs",
            $$"""
            namespace Scout;

            public sealed class PortedRgGeneratedCase
            {
                [TestMethod]
                [{{ignore}}("reason")]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, ignore);
    }

    /// <summary>
    /// Verifies ignored data rows are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsIgnoredDataRowsAsync()
    {
        string ignoreMessage = "Ignore" + "Message";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [TestMethod]
                [DataRow("case", {{ignoreMessage}} = "reason")]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "DataRow." + ignoreMessage);
    }

    /// <summary>
    /// Verifies ignored test classes are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsIgnoredTestClassesAsync()
    {
        string ignore = "Ig" + "nore";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            [{{ignore}}]
            public sealed class SampleTests
            {
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, ignore);
    }

    /// <summary>
    /// Verifies quarantine-like categories are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsQuarantineCategoriesAsync()
    {
        string category = "Test" + "Category";
        string quarantine = "Quaran" + "tine";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [{{category}}("{{quarantine}}") ]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, category);
    }

    /// <summary>
    /// Verifies runtime inconclusive calls are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsRuntimeInconclusiveCallsAsync()
    {
        string inconclusive = "Incon" + "clusive";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [TestMethod]
                public void Waived()
                {
                    Assert.{{inconclusive}}("reason");
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "Assert." + inconclusive);
    }

    /// <summary>
    /// Verifies inconclusive exceptions are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsInconclusiveExceptionsAsync()
    {
        string exception = "AssertInconclusive" + "Exception";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [TestMethod]
                public void Waived()
                {
                    throw new {{exception}}();
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, exception);
    }

    /// <summary>
    /// Verifies fixture capability returns are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsFixtureCapabilityReturnsAsync()
    {
        string probe = "TryCreate" + "DirectorySymlink";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [TestMethod]
                public void Waived()
                {
                    if ({{probe}}())
                    {
                        return;
                    }
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "fixture capability return");
    }

    /// <summary>
    /// Verifies platform guard returns are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsPlatformGuardReturnsAsync()
    {
        string platform = "Operating" + "System";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [TestMethod]
                public void Waived()
                {
                    if ({{platform}}.IsWindows())
                    {
                        return;
                    }
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "platform guard return");
    }

    /// <summary>
    /// Verifies ignored dynamic test data cannot bypass the no-skip policy.
    /// </summary>
    /// <param name="implicitCreation">Whether the data row uses target-typed construction.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReportsIgnoredDynamicDataRowsAsync(bool implicitCreation)
    {
        string property = "Ignore" + "Message";
        string creation = implicitCreation ? "new(1)" : "new TestDataRow<int>(1)";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                public static TestDataRow<int> Data()
                {
                    return {{creation}} { {{property}} = "reason" };
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "test data " + property);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(string source)
    {
        string filePath = Path.Join(Path.GetTempPath(), "ScoutAnalyzerTests", "tests", "Scout.Foundation.Tests", "SampleTests.cs");
        return await AnalyzeSourceAtPathAsync(filePath, source).ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAtPathAsync(string filePath, string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var compilation = CSharpCompilation.Create(
            "NoSkippedTestsAnalyzerTests",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new DictionaryAnalyzerConfigOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.IsTestProject"] = "true",
                }));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NoSkippedTestsAnalyzer()), options)
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
    }

    private static void AssertForbiddenWaiver(ImmutableArray<Diagnostic> diagnostics, string waiver)
    {
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("SCOUT0004", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual("Test waiver '" + waiver + "' is forbidden by Scout's no-skip policy", diagnostic.GetMessage());
    }
}
