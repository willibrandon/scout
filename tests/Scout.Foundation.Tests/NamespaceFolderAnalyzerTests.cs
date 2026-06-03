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
/// Verifies Scout namespace/folder analyzer behavior.
/// </summary>
public sealed class NamespaceFolderAnalyzerTests
{
    /// <summary>
    /// Verifies project-name namespaces are rejected at the project root.
    /// </summary>
    [Theory]
    [InlineData("Scout.App")]
    [InlineData("Scout.Automata")]
    [InlineData("Scout.Automata.AhoCorasick")]
    [InlineData("Scout.Automata.Memmem")]
    [InlineData("Scout.Automata.Syntax")]
    [InlineData("Scout.Bytes")]
    [InlineData("Scout.Cli")]
    [InlineData("Scout.Diagnostics")]
    [InlineData("Scout.Encoding")]
    [InlineData("Scout.Encoding.Io")]
    [InlineData("Scout.Errors")]
    [InlineData("Scout.Globbing")]
    [InlineData("Scout.Ignore")]
    [InlineData("Scout.Matching")]
    [InlineData("Scout.Os")]
    [InlineData("Scout.Pcre2")]
    [InlineData("Scout.Printing")]
    [InlineData("Scout.Regex")]
    [InlineData("Scout.Searching")]
    [InlineData("Scout.SourceGen")]
    public async Task ReportsProjectNameNamespaceAtProjectRootAsync(string @namespace)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeNamespaceAsync(@namespace).ConfigureAwait(true);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SCOUT0003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            $"Namespace \"{@namespace}\" does not match folder structure, expected \"Scout\"",
            diagnostic.GetMessage());
    }

    /// <summary>
    /// Verifies the repository root namespace is accepted at the project root.
    /// </summary>
    [Fact]
    public async Task AcceptsRootNamespaceAtProjectRootAsync()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeNamespaceAsync("Scout").ConfigureAwait(true);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Verifies checked-in sources use namespaces that match their project folder structure.
    /// </summary>
    [Fact]
    public async Task RepositorySourcesUseExpectedNamespacesAsync()
    {
        string root = FindRepositoryRoot();
        var mismatches = new List<string>();

        foreach (string filePath in EnumerateRepositorySources(root))
        {
            string projectDirectory = FindProjectDirectory(filePath, root);
            string source = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken).ConfigureAwait(true);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                source,
                path: filePath,
                cancellationToken: TestContext.Current.CancellationToken);
            ImmutableArray<Diagnostic> diagnostics = await AnalyzeTreeAsync(tree, projectDirectory).ConfigureAwait(true);

            foreach (Diagnostic diagnostic in diagnostics)
            {
                if (string.Equals(diagnostic.Id, "SCOUT0003", StringComparison.Ordinal))
                {
                    mismatches.Add($"{Path.GetRelativePath(root, filePath)}: {diagnostic.GetMessage()}");
                }
            }
        }

        Assert.Empty(mismatches);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeNamespaceAsync(string @namespace)
    {
        string projectDirectory = Path.Combine(Path.GetTempPath(), "Scout.Automata.Memmem");
        string filePath = Path.Combine(projectDirectory, "MemchrSearch.cs");
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
            if (File.Exists(Path.Combine(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }
}
