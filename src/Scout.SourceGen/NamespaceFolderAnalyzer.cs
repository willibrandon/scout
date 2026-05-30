using System;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

/// <summary>
/// Enforces Scout's evaluated root namespace plus folder structure for hand-written C# sources.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceFolderAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.NamespaceDoesNotMatchFolderStructure);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        string filePath = context.Tree.FilePath;
        if (ShouldSkipFile(filePath))
        {
            return;
        }

        if (!TryGetGlobalOption(context, "build_property.RootNamespace", out string rootNamespace))
        {
            return;
        }

        if (!TryGetGlobalOption(context, "build_property.ProjectDir", out string projectDirectory))
        {
            return;
        }

        string expectedNamespace = GetExpectedNamespace(rootNamespace, projectDirectory, filePath);
        if (expectedNamespace.Length == 0)
        {
            return;
        }

        CompilationUnitSyntax root = context.Tree.GetCompilationUnitRoot(context.CancellationToken);
        foreach (SyntaxNode node in root.DescendantNodes(static _ => true))
        {
            if (node is not BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                continue;
            }

            string actualNamespace = namespaceDeclaration.Name.ToString();
            if (string.Equals(actualNamespace, expectedNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.NamespaceDoesNotMatchFolderStructure,
                namespaceDeclaration.Name.GetLocation(),
                actualNamespace,
                expectedNamespace);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool TryGetGlobalOption(SyntaxTreeAnalysisContext context, string key, out string value)
    {
        value = string.Empty;
        if (!context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(key, out string? option))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(option))
        {
            return false;
        }

        value = option;
        return true;
    }

    private static bool ShouldSkipFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return true;
        }

        string normalizedPath = filePath.Replace('\\', '/');
        if (normalizedPath.Contains("/obj/", StringComparison.Ordinal) ||
            normalizedPath.Contains("/bin/", StringComparison.Ordinal))
        {
            return true;
        }

        string fileName = Path.GetFileName(filePath);
        return fileName.EndsWith(".g.cs", StringComparison.Ordinal) ||
            fileName.EndsWith(".generated.cs", StringComparison.Ordinal) ||
            fileName.EndsWith(".Designer.cs", StringComparison.Ordinal);
    }

    private static string GetExpectedNamespace(string rootNamespace, string projectDirectory, string filePath)
    {
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string relativeDirectory = GetRelativeDirectory(projectDirectory, directory);
        if (relativeDirectory.Length == 0)
        {
            return rootNamespace;
        }

        string suffix = relativeDirectory
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace(Path.AltDirectorySeparatorChar, '.');
        return $"{rootNamespace}.{suffix}";
    }

    private static string GetRelativeDirectory(string projectDirectory, string directory)
    {
        if (string.IsNullOrEmpty(projectDirectory) || string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }

        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string normalizedProjectDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(projectDirectory));
        string normalizedDirectory = Path.GetFullPath(directory);
        string trimmedProjectDirectory = normalizedProjectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedDirectory, trimmedProjectDirectory, comparison))
        {
            return string.Empty;
        }

        if (!normalizedDirectory.StartsWith(normalizedProjectDirectory, comparison))
        {
            return string.Empty;
        }

        return normalizedDirectory.Substring(normalizedProjectDirectory.Length);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
