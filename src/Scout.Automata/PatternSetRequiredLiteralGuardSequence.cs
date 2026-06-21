namespace Scout;

internal sealed class PatternSetRequiredLiteralGuardSequence
{
    private readonly PatternSetRequiredLiteralGuardElement[] elements;

    public PatternSetRequiredLiteralGuardSequence(PatternSetRequiredLiteralGuardElement[] elements)
    {
        this.elements = elements;
    }

    public bool CouldMatchFrom(ReadOnlySpan<byte> haystack, int elementIndex, int position, ref int budget)
    {
        if (--budget <= 0)
        {
            return true;
        }

        if (elementIndex == elements.Length)
        {
            return true;
        }

        PatternSetRequiredLiteralGuardElement element = elements[elementIndex];
        if (element.Kind == PatternSetRequiredLiteralGuardElementKind.Predicate)
        {
            return element.PredicateMatches(haystack, position) &&
                CouldMatchFrom(haystack, elementIndex + 1, position, ref budget);
        }

        RegexSimpleSequenceSegment segment = element.Segment;
        int maxCount = segment.Maximum ?? haystack.Length - position;
        maxCount = Math.Min(maxCount, haystack.Length - position);
        if (maxCount < segment.Minimum)
        {
            return false;
        }

        int matched = 0;
        while (matched < maxCount &&
            position + matched < haystack.Length &&
            segment.AtomMatches(haystack[position + matched]))
        {
            matched++;
        }

        if (matched < segment.Minimum)
        {
            return false;
        }

        int choices = matched - segment.Minimum + 1;
        if (!segment.Maximum.HasValue &&
            choices > PatternSetRequiredLiteralGuard.MaxUnboundedBacktrackChoices)
        {
            return true;
        }

        if (segment.Lazy)
        {
            for (int count = segment.Minimum; count <= matched; count++)
            {
                if (CouldMatchFrom(haystack, elementIndex + 1, position + count, ref budget))
                {
                    return true;
                }
            }
        }
        else
        {
            for (int count = matched; count >= segment.Minimum; count--)
            {
                if (CouldMatchFrom(haystack, elementIndex + 1, position + count, ref budget))
                {
                    return true;
                }
            }
        }

        return budget <= 0;
    }
}
