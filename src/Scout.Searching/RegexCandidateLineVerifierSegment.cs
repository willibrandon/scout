namespace Scout;

internal readonly struct RegexCandidateLineVerifierSegment(
    RegexSimpleSequenceSegment simple,
    bool requiresUtf8ScalarFallback)
{
    public int Minimum => simple.Minimum;

    public int? Maximum => simple.Maximum;

    public bool RequiresUtf8ScalarFallback => requiresUtf8ScalarFallback;

    public bool AtomMatches(byte value)
    {
        return simple.AtomMatches(value);
    }

    public bool TryAtomMatches(byte value, out bool matches, out bool completed)
    {
        if (requiresUtf8ScalarFallback && value > 0x7F)
        {
            matches = false;
            completed = false;
            return false;
        }

        matches = simple.AtomMatches(value);
        completed = true;
        return true;
    }
}
