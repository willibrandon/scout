using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Scout;

/// <summary>
/// Verifies Scout flag catalog source generation diagnostics.
/// </summary>
[TestClass]
public sealed class FlagCatalogSourceGeneratorTests
{
    /// <summary>
    /// Verifies flag definitions without pinned order metadata are rejected.
    /// </summary>
    [TestMethod]
    public void ReportsMissingFlagOrder()
    {
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(
            """
            namespace Scout
            {
                internal interface IFlag<TSelf>
                {
                }
            }

            namespace Scout.Flags.Definitions
            {
                internal readonly struct FirstFlag : Scout.IFlag<FirstFlag>
                {
                }
            }
            """);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("SCOUT0005", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual("Flag definition 'FirstFlag' must be annotated with [FlagOrder(<pinned upstream index>)]", diagnostic.GetMessage());
    }

    /// <summary>
    /// Verifies duplicate pinned order metadata is rejected before catalog generation.
    /// </summary>
    [TestMethod]
    public void ReportsDuplicateFlagOrder()
    {
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(
            """
            namespace Scout
            {
                [System.AttributeUsage(System.AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
                internal sealed class FlagOrderAttribute : System.Attribute
                {
                    public FlagOrderAttribute(int order)
                    {
                    }
                }

                internal interface IFlag<TSelf>
                {
                }
            }

            namespace Scout.Flags.Definitions
            {
                [Scout.FlagOrder(0)]
                internal readonly struct FirstFlag : Scout.IFlag<FirstFlag>
                {
                }

                [Scout.FlagOrder(0)]
                internal readonly struct SecondFlag : Scout.IFlag<SecondFlag>
                {
                }
            }
            """);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("SCOUT0006", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual("Flag definitions 'FirstFlag' and 'SecondFlag' both declare pinned upstream order 0", diagnostic.GetMessage());
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp14),
            path: "FlagDefinitions.cs");
        var compilation = CSharpCompilation.Create(
            "FlagCatalogSourceGeneratorTests",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FlagCatalogSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);
        return diagnostics;
    }
}
