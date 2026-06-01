using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Scout;

/// <summary>
/// Verifies Scout flag catalog source generation diagnostics.
/// </summary>
public sealed class FlagCatalogSourceGeneratorTests
{
    /// <summary>
    /// Verifies flag definitions without pinned order metadata are rejected.
    /// </summary>
    [Fact]
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

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SCOUT0005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Flag definition 'FirstFlag' must be annotated with [FlagOrder(<pinned upstream index>)]", diagnostic.GetMessage());
    }

    /// <summary>
    /// Verifies duplicate pinned order metadata is rejected before catalog generation.
    /// </summary>
    [Fact]
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

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SCOUT0006", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Flag definitions 'FirstFlag' and 'SecondFlag' both declare pinned upstream order 0", diagnostic.GetMessage());
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
