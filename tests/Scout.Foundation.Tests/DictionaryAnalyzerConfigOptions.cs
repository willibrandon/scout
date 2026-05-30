using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

internal sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly IReadOnlyDictionary<string, string> values;

    public DictionaryAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
    {
        this.values = values;
    }

    public override bool TryGetValue(string key, out string value)
    {
        if (values.TryGetValue(key, out string? candidate))
        {
            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
