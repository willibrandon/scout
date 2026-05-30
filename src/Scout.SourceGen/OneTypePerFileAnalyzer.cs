using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

/// <summary>
/// Enforces Scout's one-type-per-file rule for hand-written and generated C# sources.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneTypePerFileAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            DiagnosticDescriptors.MoreThanOneType,
            DiagnosticDescriptors.TypeNameDoesNotMatchFileName);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        if (IsExternalGeneratedInterop(context.Tree.FilePath))
        {
            return;
        }

        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);
        List<SyntaxNode> declarations = new(capacity: 2);

        foreach (SyntaxNode node in root.DescendantNodes(static _ => true))
        {
            if (IsTypeDeclaration(node))
            {
                declarations.Add(node);
            }
        }

        if (declarations.Count == 0)
        {
            return;
        }

        if (declarations.Count > 1)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.MoreThanOneType,
                declarations[1].GetLocation(),
                context.Tree.FilePath,
                declarations.Count);
            context.ReportDiagnostic(diagnostic);
            return;
        }

        SyntaxNode declaration = declarations[0];
        string expectedName = GetTypeName(declaration);
        string actualName = GetComparableFileStem(context.Tree.FilePath);
        if (!string.Equals(expectedName, actualName, StringComparison.Ordinal))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.TypeNameDoesNotMatchFileName,
                declaration.GetLocation(),
                expectedName);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsTypeDeclaration(SyntaxNode node)
    {
        return node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;
    }

    private static string GetTypeName(SyntaxNode node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier.ValueText,
            DelegateDeclarationSyntax declaration => declaration.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private static string GetComparableFileStem(string filePath)
    {
        string stem = Path.GetFileNameWithoutExtension(filePath);
        if (stem.EndsWith(".g", StringComparison.Ordinal))
        {
            stem = Path.GetFileNameWithoutExtension(stem);
        }

        return stem;
    }

    private static bool IsExternalGeneratedInterop(string filePath)
    {
        string normalizedPath = filePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        return normalizedPath.Contains("/Microsoft.Interop.LibraryImportGenerator/", StringComparison.Ordinal);
    }
}
