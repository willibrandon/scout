using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

/// <summary>
/// Verifies Scout one-type-per-file analyzer behavior.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[TestClass]
public sealed class OneTypePerFileAnalyzerTests(TestContext testContext)
{
    private static readonly string[] SourceRootNames = ["src", "tests"];

    /// <summary>
    /// Verifies a single type with a matching file name is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsSingleMatchingTypeAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            "Parser.cs",
            """
            namespace Scout;

            public sealed class Parser
            {
            }
            """).ConfigureAwait(true);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies files declaring multiple top-level types are rejected.
    /// </summary>
    [TestMethod]
    public async Task ReportsMultipleTopLevelTypesAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            "Parser.cs",
            """
            namespace Scout;

            public sealed class Parser
            {
            }

            public sealed class OtherParser
            {
            }
            """).ConfigureAwait(true);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("SCOUT0001", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("declares 2 types", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies nested types count against the one-type-per-file rule.
    /// </summary>
    [TestMethod]
    public async Task ReportsNestedTypesAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            "Parser.cs",
            """
            namespace Scout;

            public sealed class Parser
            {
                private sealed class NestedParser
                {
                }
            }
            """).ConfigureAwait(true);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("SCOUT0001", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("declares 2 types", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies delegates count as type declarations.
    /// </summary>
    [TestMethod]
    public async Task ReportsDelegatesAsTypesAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            "Parser.cs",
            """
            namespace Scout;

            public sealed class Parser
            {
            }

            public delegate void ParserFactory();
            """).ConfigureAwait(true);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("SCOUT0001", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("declares 2 types", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a single type must match its file name.
    /// </summary>
    [TestMethod]
    public async Task ReportsTypeNameFileNameMismatchAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            "Parser.cs",
            """
            namespace Scout;

            public sealed class SearchParser
            {
            }
            """).ConfigureAwait(true);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("SCOUT0002", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual("Type 'SearchParser' must live in a file named 'SearchParser.cs'", diagnostic.GetMessage());
    }

    /// <summary>
    /// Verifies generated files compare against the pre-.g file stem.
    /// </summary>
    [TestMethod]
    public async Task AcceptsGeneratedFileStemAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAsync(
            "GeneratedFlagCatalog.g.cs",
            """
            namespace Scout;

            public static partial class GeneratedFlagCatalog
            {
            }
            """).ConfigureAwait(true);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies external LibraryImport-generated files are outside Scout's structural policy.
    /// </summary>
    [TestMethod]
    public async Task IgnoresExternalLibraryImportGeneratedFilesAsync()
    {
        string filePath = Path.Join(
            Path.GetTempPath(),
            "ScoutAnalyzerTests",
            "obj",
            "Debug",
            "net9.0",
            "Microsoft.Interop.LibraryImportGenerator",
            "Microsoft.Interop.LibraryImportGenerator",
            "LibraryImports.g.cs");
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAtPathAsync(
            filePath,
            """
            namespace Scout;

            public static partial class FirstLibraryImport
            {
            }

            public static partial class SecondLibraryImport
            {
            }
            """).ConfigureAwait(true);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies checked-in sources obey the one-type-per-file rule.
    /// </summary>
    [TestMethod]
    public async Task RepositorySourcesUseOneTypePerFileAsync()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string filePath in EnumerateRepositorySources(root))
        {
            string source = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(true);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                source,
                path: filePath,
                cancellationToken: cancellationToken);
            ImmutableArray<Diagnostic> diagnostics = await AnalyzeTreeAsync(tree).ConfigureAwait(true);

            foreach (Diagnostic diagnostic in diagnostics.Where(diagnostic => string.Equals(diagnostic.Id, "SCOUT0001", StringComparison.Ordinal) ||
                    string.Equals(diagnostic.Id, "SCOUT0002", StringComparison.Ordinal)))
            {
                violations.Add($"{Path.GetRelativePath(root, filePath)}: {diagnostic.GetMessage()}");
            }
        }

        Assert.IsEmpty(violations);
    }

    /// <summary>
    /// Verifies MTP infrastructure generated by the test SDK is outside Scout's structural policy.
    /// </summary>
    /// <param name="fileName">The generated infrastructure source name.</param>
    [TestMethod]
    [DataRow("MicrosoftTestingPlatformEntryPoint.cs")]
    [DataRow("SelfRegisteredExtensions.cs")]
    public async Task IgnoresExternalTestingPlatformGeneratedFilesAsync(string fileName)
    {
        string filePath = Path.Join(Path.GetTempPath(), "ScoutAnalyzerTests", "obj", "Debug", "net10.0", fileName);
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeSourceAtPathAsync(
            filePath,
            """
            namespace Scout;

            public sealed class TestEntryPoint
            {
            }

            public sealed class TestExtensionRegistration
            {
            }
            """).ConfigureAwait(true);

        Assert.IsEmpty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(string fileName, string source)
    {
        string filePath = Path.Join(Path.GetTempPath(), "ScoutAnalyzerTests", fileName);
        return AnalyzeSourceAtPathAsync(filePath, source);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAtPathAsync(string filePath, string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        return await AnalyzeTreeAsync(tree).ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeTreeAsync(SyntaxTree tree)
    {
        var compilation = CSharpCompilation.Create(
            "OneTypePerFileAnalyzerTests",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new OneTypePerFileAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
    }

    private static IEnumerable<string> EnumerateRepositorySources(string root)
    {
        foreach (string sourceRoot in SourceRootNames.Select(name => Path.Join(root, name)))
        {
            foreach (string filePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(filePath => !IsBuildArtifact(filePath)))
            {
                yield return filePath;
            }
        }
    }

    private static bool IsBuildArtifact(string filePath)
    {
        string normalizedPath = filePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        return normalizedPath.Contains("/obj/", StringComparison.Ordinal) ||
            normalizedPath.Contains("/bin/", StringComparison.Ordinal);
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
