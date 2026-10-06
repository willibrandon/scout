using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

/// <summary>
/// Verifies Scout namespace/folder analyzer behavior.
/// </summary>
/// <param name="testContext">The context for the current test.</param>
[TestClass]
public sealed class NamespaceFolderAnalyzerTests(TestContext testContext)
{
    private static readonly string[] SourceRootNames = ["src", "tests"];

    /// <summary>
    /// Verifies project-name namespaces are rejected at the project root.
    /// </summary>
    [TestMethod]
    [DataRow("Scout.App")]
    [DataRow("Scout.Automata")]
    [DataRow("Scout.Automata.AhoCorasick")]
    [DataRow("Scout.Automata.Memmem")]
    [DataRow("Scout.Automata.Syntax")]
    [DataRow("Scout.Bytes")]
    [DataRow("Scout.Cli")]
    [DataRow("Scout.Diagnostics")]
    [DataRow("Scout.Encoding")]
    [DataRow("Scout.Encoding.Io")]
    [DataRow("Scout.Errors")]
    [DataRow("Scout.Globbing")]
    [DataRow("Scout.Ignore")]
    [DataRow("Scout.Matching")]
    [DataRow("Scout.Os")]
    [DataRow("Scout.Pcre2")]
    [DataRow("Scout.Printing")]
    [DataRow("Scout.Regex")]
    [DataRow("Scout.Searching")]
    [DataRow("Scout.SourceGen")]
    public async Task ReportsProjectNameNamespaceAtProjectRootAsync(string @namespace)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeNamespaceAsync(@namespace).ConfigureAwait(true);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("SCOUT0003", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual(
            $"Namespace \"{@namespace}\" does not match folder structure, expected \"Scout\"",
            diagnostic.GetMessage());
    }

    /// <summary>
    /// Verifies the repository root namespace is accepted at the project root.
    /// </summary>
    [TestMethod]
    public async Task AcceptsRootNamespaceAtProjectRootAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeNamespaceAsync("Scout").ConfigureAwait(true);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies checked-in sources use namespaces that match their project folder structure.
    /// </summary>
    [TestMethod]
    public async Task RepositorySourcesUseExpectedNamespacesAsync()
    {
        CancellationToken cancellationToken = testContext.CancellationToken;
        string root = FindRepositoryRoot();
        var mismatches = new List<string>();

        foreach (string filePath in EnumerateRepositorySources(root))
        {
            string projectDirectory = FindProjectDirectory(filePath, root);
            string source = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(true);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                source,
                path: filePath,
                cancellationToken: cancellationToken);
            ImmutableArray<Diagnostic> diagnostics = await AnalyzeTreeAsync(tree, projectDirectory).ConfigureAwait(true);

            foreach (Diagnostic diagnostic in diagnostics.Where(diagnostic => string.Equals(diagnostic.Id, "SCOUT0003", StringComparison.Ordinal)))
            {
                mismatches.Add($"{Path.GetRelativePath(root, filePath)}: {diagnostic.GetMessage()}");
            }
        }

        Assert.IsEmpty(mismatches);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeNamespaceAsync(string @namespace)
    {
        string projectDirectory = Path.Join(Path.GetTempPath(), "Scout.Automata.Memmem");
        string filePath = Path.Join(projectDirectory, "MemchrSearch.cs");
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            $$"""
            namespace {{@namespace}};

            public static class MemchrSearch
            {
            }
            """,
            path: filePath);

        return await AnalyzeTreeAsync(tree, projectDirectory).ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeTreeAsync(SyntaxTree tree, string projectDirectory)
    {
        var compilation = CSharpCompilation.Create(
            "NamespaceFolderAnalyzerTests",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new DictionaryAnalyzerConfigOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.RootNamespace"] = "Scout",
                    ["build_property.ProjectDir"] = projectDirectory,
                }));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NamespaceFolderAnalyzer()), options)
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

    private static string FindProjectDirectory(string filePath, string root)
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(filePath) ?? root);
        while (directory is not null)
        {
            if (Directory.GetFiles(directory.FullName, "*.csproj").Length > 0)
            {
                return directory.FullName;
            }

            if (string.Equals(directory.FullName, root, StringComparison.Ordinal))
            {
                break;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate a project for '{filePath}'.");
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
