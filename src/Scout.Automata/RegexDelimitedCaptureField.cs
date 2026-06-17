namespace Scout;

internal sealed class RegexDelimitedCaptureField
{
    private const int LookupMatcher = 0;
    private const int AnyExceptMatcher = 1;
    private const int DigitMatcher = 2;
    private const int NotDigitMatcher = 3;
    private const int UppercaseAsciiOrDigitMatcher = 4;
    private const int OneOfTwoMatcher = 5;
    private const int DigitDashSlashMatcher = 6;

    private readonly bool[]? matches;
    private readonly int matcherKind;
    private readonly byte first;
    private readonly byte second;

    public RegexDelimitedCaptureField(int captureIndex, int minimum, int? maximum, bool[] matches)
        : this(captureIndex, minimum, maximum, LookupMatcher, first: 0, second: 0, matches)
    {
    }

    private RegexDelimitedCaptureField(
        int captureIndex,
        int minimum,
        int? maximum,
        int matcherKind,
        byte first,
        byte second,
        bool[]? matches)
    {
        CaptureIndex = captureIndex;
        Minimum = minimum;
        Maximum = maximum;
        this.matcherKind = matcherKind;
        this.first = first;
        this.second = second;
        this.matches = matches;
    }

    public int CaptureIndex { get; }

    public int Minimum { get; }

    public int? Maximum { get; }

    public bool Matches(byte value)
    {
        if (matches is not null)
        {
            return matches[value];
        }

        return matcherKind switch
        {
            AnyExceptMatcher => value != first,
            DigitMatcher => value is >= (byte)'0' and <= (byte)'9',
            NotDigitMatcher => value is < (byte)'0' or > (byte)'9',
            UppercaseAsciiOrDigitMatcher => value is >= (byte)'A' and <= (byte)'Z' ||
                value is >= (byte)'0' and <= (byte)'9',
            OneOfTwoMatcher => value == first || value == second,
            DigitDashSlashMatcher => value is >= (byte)'0' and <= (byte)'9' ||
                value is (byte)'-' or (byte)'/',
            _ => false,
        };
    }

    public static bool TryCreateSpecialized(
        int captureIndex,
        int minimum,
        int? maximum,
        RegexSyntaxKind kind,
        ReadOnlySpan<byte> expression,
        RegexCompileOptions options,
        out RegexDelimitedCaptureField? field)
    {
        field = null;
        if (options.CaseInsensitive)
        {
            return false;
        }

        switch (kind)
        {
            case RegexSyntaxKind.DigitClass:
                field = Create(captureIndex, minimum, maximum, DigitMatcher);
                return true;
            case RegexSyntaxKind.NotDigitClass:
                field = Create(captureIndex, minimum, maximum, NotDigitMatcher);
                return true;
            case RegexSyntaxKind.CharacterClass:
                return TryCreateCharacterClass(captureIndex, minimum, maximum, expression, out field);
            default:
                return false;
        }
    }

    private static bool TryCreateCharacterClass(
        int captureIndex,
        int minimum,
        int? maximum,
        ReadOnlySpan<byte> expression,
        out RegexDelimitedCaptureField? field)
    {
        field = null;
        if (expression.Length == 2 && expression[0] == (byte)'^')
        {
            field = Create(captureIndex, minimum, maximum, AnyExceptMatcher, expression[1]);
            return true;
        }

        if (expression.SequenceEqual("0-9"u8))
        {
            field = Create(captureIndex, minimum, maximum, DigitMatcher);
            return true;
        }

        if (expression.SequenceEqual("A-Z0-9"u8))
        {
            field = Create(captureIndex, minimum, maximum, UppercaseAsciiOrDigitMatcher);
            return true;
        }

        if (expression.SequenceEqual("YN"u8))
        {
            field = Create(captureIndex, minimum, maximum, OneOfTwoMatcher, (byte)'Y', (byte)'N');
            return true;
        }

        if (expression.SequenceEqual("-0-9/"u8))
        {
            field = Create(captureIndex, minimum, maximum, DigitDashSlashMatcher);
            return true;
        }

        return false;
    }

    private static RegexDelimitedCaptureField Create(
        int captureIndex,
        int minimum,
        int? maximum,
        int matcherKind,
        byte first = 0,
        byte second = 0)
    {
        return new RegexDelimitedCaptureField(captureIndex, minimum, maximum, matcherKind, first, second, matches: null);
    }
}
