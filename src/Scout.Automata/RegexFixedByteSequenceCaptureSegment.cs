namespace Scout;

internal readonly struct RegexFixedByteSequenceCaptureSegment
{
    private readonly RegexSimpleSequenceSegment[] atoms;

    public RegexFixedByteSequenceCaptureSegment(
        int captureIndex,
        RegexSimpleSequenceSegment[] atoms,
        bool optional)
    {
        CaptureIndex = captureIndex;
        this.atoms = atoms;
        Optional = optional;
    }

    public int CaptureIndex { get; }

    public bool Optional { get; }

    public int Length => atoms.Length;

    public bool Matches(ReadOnlySpan<byte> haystack, int position)
    {
        if (atoms.Length > haystack.Length - position)
        {
            return false;
        }

        for (int index = 0; index < atoms.Length; index++)
        {
            if (!atoms[index].AtomMatches(haystack[position + index]))
            {
                return false;
            }
        }

        return true;
    }
}
