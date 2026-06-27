namespace Scout;

internal sealed class RegexCandidateLineAccelerator
{
    private readonly RegexPrefilter prefilter;
    private readonly RegexCandidateLineVerifier? verifier;

    private RegexCandidateLineAccelerator(RegexPrefilter prefilter, RegexCandidateLineVerifier? verifier)
    {
        this.prefilter = prefilter;
        this.verifier = verifier;
    }

    public static bool TryCompile(
        ReadOnlySpan<byte> pattern,
        bool asciiCaseInsensitive,
        out RegexCandidateLineAccelerator? accelerator)
    {
        accelerator = null;
        if (pattern.IsEmpty || pattern.Contains((byte)'\n'))
        {
            return false;
        }

        RegexSyntaxTree tree;
        try
        {
            tree = RegexSyntaxParser.Parse(pattern);
        }
        catch (FormatException)
        {
            return false;
        }

        var options = new RegexCompileOptions(
            asciiCaseInsensitive,
            swapGreed: false,
            multiLine: false,
            dotMatchesNewline: false);
        var prefilter = RegexPrefilter.Compile(tree.Root, options);
        if (prefilter is null ||
            prefilter.UsesRequiredLiteralWindow ||
            prefilter.Kind is not (RegexPrefilterKind.Memmem or RegexPrefilterKind.Teddy or RegexPrefilterKind.AhoCorasick))
        {
            return false;
        }

        RegexCandidateLineVerifier.TryCompile(tree.Root, options, out RegexCandidateLineVerifier? verifier);
        accelerator = new RegexCandidateLineAccelerator(prefilter, verifier);
        return true;
    }

    public bool HasVerifier => verifier is not null;

    public int FindCandidate(ReadOnlySpan<byte> haystack, int startAt)
    {
        int searchAt = startAt;
        while (searchAt < haystack.Length)
        {
            int candidate = prefilter.FindCandidate(haystack, searchAt);
            if (candidate < 0)
            {
                return -1;
            }

            if (verifier is null ||
                verifier.CanStartAt(haystack, candidate, out bool completed) ||
                !completed)
            {
                return candidate;
            }

            searchAt = candidate + 1;
        }

        return -1;
    }

    public bool TryMatchAt(ReadOnlySpan<byte> haystack, int start, out int length, out bool completed)
    {
        length = 0;
        completed = verifier is null;
        return verifier?.TryMatchAt(haystack, start, out length, out completed) == true;
    }
}
