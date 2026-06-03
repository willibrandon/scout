using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

internal sealed class DictionaryAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private static readonly AnalyzerConfigOptions EmptyOptions = new DictionaryAnalyzerConfigOptions(
        new Dictionary<string, string>());

    private readonly AnalyzerConfigOptions globalOptions;

    public DictionaryAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions)
    {
        this.globalOptions = new DictionaryAnalyzerConfigOptions(globalOptions);
    }

    public override AnalyzerConfigOptions GlobalOptions => globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return EmptyOptions;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return EmptyOptions;
    }
}
