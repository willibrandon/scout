using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Scout;

/// <summary>
/// Identifies hot-path LINQ calls while distinguishing framework methods with the same names.
/// </summary>
internal static class LinqUsageInspector
{
    private static readonly HashSet<string> s_methodNames =
    [
        "Select", "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "GroupBy", "Join", "GroupJoin", "Zip", "Aggregate",
    ];

    private static readonly MetadataReference[] s_references =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();

    /// <summary>
    /// Finds LINQ calls and query expressions in a source file.
    /// </summary>
    /// <param name="source">The C# source.</param>
    /// <returns>The prohibited expressions.</returns>
    internal static IEnumerable<string> Find(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "HotPathInspection",
            [tree, CSharpSyntaxTree.ParseText("global using System; global using System.IO; global using System.Linq;")],
            s_references);
        SemanticModel model = compilation.GetSemanticModel(tree);
        foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
        {
            if (node is QueryExpressionSyntax)
            {
                yield return node.ToString();
            }
            else if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation &&
                s_methodNames.Contains(member.Name.Identifier.ValueText))
            {
                var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (method is null || method.ContainingNamespace.ToDisplayString() == "System.Linq")
                {
                    yield return invocation.ToString();
                }
            }
        }
    }
}
