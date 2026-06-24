namespace Scout;

internal readonly record struct RegexBoundedDigitDelimiterRun(int Minimum, int Maximum, int LengthMask)
{
    public static RegexBoundedDigitDelimiterRun Empty => new(0, 0, 1);

    public bool Allows(int length)
    {
        return length >= 0 && (LengthMask & (1 << length)) != 0;
    }

    public static RegexBoundedDigitDelimiterRun Range(int minimum, int maximum)
    {
        int mask = 0;
        for (int length = minimum; length <= maximum; length++)
        {
            mask |= 1 << length;
        }

        return new RegexBoundedDigitDelimiterRun(minimum, maximum, mask);
    }

    public static RegexBoundedDigitDelimiterRun Concatenate(
        RegexBoundedDigitDelimiterRun left,
        RegexBoundedDigitDelimiterRun right)
    {
        int mask = 0;
        for (int leftLength = left.Minimum; leftLength <= left.Maximum; leftLength++)
        {
            if (!left.Allows(leftLength))
            {
                continue;
            }

            for (int rightLength = right.Minimum; rightLength <= right.Maximum; rightLength++)
            {
                if (right.Allows(rightLength))
                {
                    mask |= 1 << (leftLength + rightLength);
                }
            }
        }

        return new RegexBoundedDigitDelimiterRun(left.Minimum + right.Minimum, left.Maximum + right.Maximum, mask);
    }

    public static RegexBoundedDigitDelimiterRun Repeat(
        RegexBoundedDigitDelimiterRun child,
        int minimum,
        int maximum)
    {
        RegexBoundedDigitDelimiterRun result = Empty;
        for (int count = 0; count < minimum; count++)
        {
            result = Concatenate(result, child);
        }

        RegexBoundedDigitDelimiterRun optional = result;
        RegexBoundedDigitDelimiterRun repeated = result;
        for (int count = minimum; count < maximum; count++)
        {
            repeated = Concatenate(repeated, child);
            optional = Union(optional, repeated);
        }

        return optional;
    }

    private static RegexBoundedDigitDelimiterRun Union(
        RegexBoundedDigitDelimiterRun left,
        RegexBoundedDigitDelimiterRun right)
    {
        return new RegexBoundedDigitDelimiterRun(
            Math.Min(left.Minimum, right.Minimum),
            Math.Max(left.Maximum, right.Maximum),
            left.LengthMask | right.LengthMask);
    }
}
