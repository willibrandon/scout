namespace Scout;

internal readonly struct RegexCandidateLineVerifierSegment(
    RegexSimpleSequenceSegment simple,
    bool requiresUtf8ScalarFallback)
{
    public int Minimum => simple.Minimum;

    public int? Maximum => simple.Maximum;

    public bool Lazy => simple.Lazy;

    public bool HasVariableLength => Maximum is null || Maximum.Value != Minimum;

    public bool RequiresUtf8ScalarFallback => requiresUtf8ScalarFallback;

    public bool IsAsciiDisjointFrom(RegexCandidateLineVerifierSegment other)
    {
        for (int value = 0; value <= 0x7F; value++)
        {
            byte byteValue = (byte)value;
            if (AtomMatches(byteValue) && other.AtomMatches(byteValue))
            {
                return false;
            }
        }

        return true;
    }

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
