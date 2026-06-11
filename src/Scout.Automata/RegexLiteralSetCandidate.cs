namespace Scout;

internal readonly struct RegexLiteralSetCandidate(int literalId, RegexMatch match)
{
    public int LiteralId { get; } = literalId;

    public RegexMatch Match { get; } = match;
}
