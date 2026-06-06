namespace Scout;

internal readonly struct IgnoreGlobSetSummary
{
    public IgnoreGlobSetSummary(
        int literals,
        int basenames,
        int extensions,
        int prefixes,
        int suffixes,
        int requiredExtensions,
        int regexes)
    {
        Literals = literals;
        Basenames = basenames;
        Extensions = extensions;
        Prefixes = prefixes;
        Suffixes = suffixes;
        RequiredExtensions = requiredExtensions;
        Regexes = regexes;
    }

    public int Literals { get; }

    public int Basenames { get; }

    public int Extensions { get; }

    public int Prefixes { get; }

    public int Suffixes { get; }

    public int RequiredExtensions { get; }

    public int Regexes { get; }

    public IgnoreGlobSetSummary Add(IgnoreGlobSetSummary other)
    {
        return new IgnoreGlobSetSummary(
            Literals + other.Literals,
            Basenames + other.Basenames,
            Extensions + other.Extensions,
            Prefixes + other.Prefixes,
            Suffixes + other.Suffixes,
            RequiredExtensions + other.RequiredExtensions,
            Regexes + other.Regexes);
    }

    public override string ToString()
    {
        return $"{Literals} literals, {Basenames} basenames, {Extensions} extensions, {Prefixes} prefixes, {Suffixes} suffixes, {RequiredExtensions} required extensions, {Regexes} regexes";
    }
}
