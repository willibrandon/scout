namespace Scout;

internal sealed class RegexCorpusCase
{
    public RegexCorpusCase(string name, string pattern, string haystack, RegexMatch? expectedMatch)
    {
        Name = name;
        Pattern = pattern;
        Haystack = haystack;
        ExpectedMatch = expectedMatch;
    }

    public string Name { get; }

    public string Pattern { get; }

    public string Haystack { get; }

    public RegexMatch? ExpectedMatch { get; }
}
