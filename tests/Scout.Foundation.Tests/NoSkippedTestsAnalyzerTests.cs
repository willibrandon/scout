using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

/// <summary>
/// Verifies Scout's no-skipped-tests analyzer behavior.
/// </summary>
public sealed class NoSkippedTestsAnalyzerTests
{
    /// <summary>
    /// Verifies a normal test attribute is accepted.
    /// </summary>
    [Fact]
    public async Task AcceptsNormalTestsAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            """
            namespace Scout;

            public sealed class SampleTests
            {
                [Fact]
                public void Passes()
                {
                }
            }
            """).ConfigureAwait(true);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies xUnit skip attributes are rejected.
    /// </summary>
    [Fact]
    public async Task ReportsXunitSkipAttributesAsync()
    {
        string skip = "Sk" + "ip";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [Fact({{skip}} = "reason")]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "Fact." + skip);
    }

    /// <summary>
    /// Verifies generated test sources are rejected even when their hint path is outside the tests folder.
    /// </summary>
    [Fact]
    public async Task ReportsGeneratedXunitSkipAttributesInTestProjectsAsync()
    {
        string skip = "Sk" + "ip";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAtPathAsync(
            "PortedRgGeneratedCase.g.cs",
            $$"""
            namespace Scout;

            public sealed class PortedRgGeneratedCase
            {
                [Fact({{skip}} = "reason")]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "Fact." + skip);
    }

    /// <summary>
    /// Verifies xUnit explicit attributes are rejected.
    /// </summary>
    [Fact]
    public async Task ReportsXunitExplicitAttributesAsync()
    {
        string explicitArgument = "Exp" + "licit";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [Theory({{explicitArgument}} = true)]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "Theory." + explicitArgument);
    }

    /// <summary>
    /// Verifies explicit or ignored test attributes are rejected.
    /// </summary>
    [Fact]
    public async Task ReportsExplicitOrIgnoredAttributesAsync()
    {
        string explicitAttribute = "Exp" + "licit";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [{{explicitAttribute}}]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, explicitAttribute);
    }

    /// <summary>
    /// Verifies quarantine-like traits are rejected.
    /// </summary>
    [Fact]
    public async Task ReportsQuarantineTraitsAsync()
    {
        string trait = "Tra" + "it";
        string quarantine = "Quaran" + "tine";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [{{trait}}("Status", "{{quarantine}}")]
                public void Waived()
                {
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, trait);
    }

    /// <summary>
    /// Verifies runtime skip calls are rejected.
    /// </summary>
    [Fact]
    public async Task ReportsRuntimeSkipCallsAsync()
    {
        string skip = "Sk" + "ip";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [Fact]
                public void Waived()
                {
                    Assert.{{skip}}("reason");
                }
            }
            """).ConfigureAwait(true);

        AssertForbiddenWaiver(diagnostics, "Assert." + skip);
    }

    /// <summary>
    /// Verifies skip exceptions are rejected.
    /// </summary>
    [Fact]
    public async Task ReportsSkipExceptionsAsync()
    {
        string exception = "Sk" + "ipException";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [Fact]
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
    [Fact]
    public async Task ReportsFixtureCapabilityReturnsAsync()
    {
        string probe = "TryCreate" + "DirectorySymlink";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [Fact]
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
    [Fact]
    public async Task ReportsPlatformGuardReturnsAsync()
    {
        string platform = "Operating" + "System";
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            $$"""
            namespace Scout;

            public sealed class SampleTests
            {
                [Fact]
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

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(string source)
    {
        string filePath = Path.Combine(Path.GetTempPath(), "ScoutAnalyzerTests", "tests", "Scout.Foundation.Tests", "SampleTests.cs");
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
        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SCOUT0004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Test waiver '" + waiver + "' is forbidden by Scout's no-skip policy", diagnostic.GetMessage());
    }
}
