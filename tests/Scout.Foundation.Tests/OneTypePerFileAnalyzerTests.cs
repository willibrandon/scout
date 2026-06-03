using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

/// <summary>
/// Verifies Scout one-type-per-file analyzer behavior.
/// </summary>
public sealed class OneTypePerFileAnalyzerTests
{
    /// <summary>
    /// Verifies a single type with a matching file name is accepted.
    /// </summary>
    [Fact]
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

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies files declaring multiple top-level types are rejected.
    /// </summary>
    [Fact]
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

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SCOUT0001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("declares 2 types", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies nested types count against the one-type-per-file rule.
    /// </summary>
    [Fact]
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

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SCOUT0001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("declares 2 types", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies delegates count as type declarations.
    /// </summary>
    [Fact]
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

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SCOUT0001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("declares 2 types", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a single type must match its file name.
    /// </summary>
    [Fact]
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

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SCOUT0002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Type 'SearchParser' must live in a file named 'SearchParser.cs'", diagnostic.GetMessage());
    }

    /// <summary>
    /// Verifies generated files compare against the pre-.g file stem.
    /// </summary>
    [Fact]
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

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies external LibraryImport-generated files are outside Scout's structural policy.
    /// </summary>
    [Fact]
    public async Task IgnoresExternalLibraryImportGeneratedFilesAsync()
    {
        string filePath = Path.Combine(
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

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies checked-in sources obey the one-type-per-file rule.
    /// </summary>
    [Fact]
    public async Task RepositorySourcesUseOneTypePerFileAsync()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string filePath in EnumerateRepositorySources(root))
        {
            string source = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken).ConfigureAwait(true);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                source,
                path: filePath,
                cancellationToken: TestContext.Current.CancellationToken);
            ImmutableArray<Diagnostic> diagnostics = await AnalyzeTreeAsync(tree).ConfigureAwait(true);

            foreach (Diagnostic diagnostic in diagnostics)
            {
                if (string.Equals(diagnostic.Id, "SCOUT0001", StringComparison.Ordinal) ||
                    string.Equals(diagnostic.Id, "SCOUT0002", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(root, filePath)}: {diagnostic.GetMessage()}");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(string fileName, string source)
    {
        string filePath = Path.Combine(Path.GetTempPath(), "ScoutAnalyzerTests", fileName);
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
        foreach (string sourceRootName in new[] { "src", "tests" })
        {
            string sourceRoot = Path.Combine(root, sourceRootName);
            foreach (string filePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsBuildArtifact(filePath))
                {
                    yield return filePath;
                }
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
            if (File.Exists(Path.Combine(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }
}
