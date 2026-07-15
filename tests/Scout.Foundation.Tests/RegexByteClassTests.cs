using System.Buffers;
using System.Text;

namespace Scout;

/// <summary>
/// Verifies byte-class scalar and predicate behavior.
/// </summary>
public sealed class RegexByteClassTests
{
    private static readonly RegexSyntaxKind[] s_wordBoundaryKinds =
    [
        RegexSyntaxKind.WordBoundary,
        RegexSyntaxKind.NotWordBoundary,
        RegexSyntaxKind.WordStartBoundary,
        RegexSyntaxKind.WordEndBoundary,
        RegexSyntaxKind.WordStartHalfBoundary,
        RegexSyntaxKind.WordEndHalfBoundary,
    ];

    /// <summary>
    /// Verifies ASCII boundary fast paths agree with scalar decoding at edges and around valid,
    /// incomplete, and malformed UTF-8 sequences.
    /// </summary>
    [Fact]
    public void UnicodeWordBoundaryAsciiFastPathsMatchScalarReference()
    {
        byte[][] haystacks =
        [
            [],
            "a"u8.ToArray(),
            "_ "u8.ToArray(),
            " a_"u8.ToArray(),
            "\u00E9"u8.ToArray(),
            "\u00E9x"u8.ToArray(),
            "x\u00E9"u8.ToArray(),
            "\u03C0_x"u8.ToArray(),
            "\U0001F4A9x"u8.ToArray(),
            [0xC3, (byte)'x'],
            [(byte)'x', 0x80],
            [0xE2, 0x82],
            [0xF0, 0x9F, (byte)'x'],
            [0xF5, 0x80, 0x80, 0x80, (byte)'x'],
            [0xFF, (byte)'a'],
            [(byte)'a', 0xFF],
        ];

        foreach (byte[] haystack in haystacks)
        {
            for (int position = 0; position <= haystack.Length; position++)
            {
                Assert.Equal(
                    ReferenceIsUtf8Boundary(haystack, position),
                    RegexByteClass.IsUtf8Boundary(haystack, position));

                foreach (RegexSyntaxKind kind in s_wordBoundaryKinds)
                {
                    bool expected = ReferencePredicateMatches(haystack, position, kind);
                    bool actual = RegexByteClass.PredicateMatches(
                        haystack,
                        position,
                        kind,
                        multiLine: false,
                        crlf: false,
                        lineTerminator: (byte)'\n',
                        utf8: true,
                        unicodeClasses: true);

                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    private static bool ReferencePredicateMatches(
        ReadOnlySpan<byte> haystack,
        int position,
        RegexSyntaxKind kind)
    {
        if (!ReferenceIsUtf8Boundary(haystack, position))
        {
            return false;
        }

        bool leftIsWord = ReferenceIsRegexWordBefore(haystack, position);
        bool rightIsWord = ReferenceIsRegexWordAt(haystack, position);
        return kind switch
        {
            RegexSyntaxKind.WordBoundary => leftIsWord != rightIsWord,
            RegexSyntaxKind.NotWordBoundary =>
                ReferenceIsRegexWordContextValid(haystack, position) && leftIsWord == rightIsWord,
            RegexSyntaxKind.WordStartBoundary => !leftIsWord && rightIsWord,
            RegexSyntaxKind.WordEndBoundary => leftIsWord && !rightIsWord,
            RegexSyntaxKind.WordStartHalfBoundary => !leftIsWord,
            RegexSyntaxKind.WordEndHalfBoundary => !rightIsWord,
            _ => false,
        };
    }

    private static bool ReferenceIsUtf8Boundary(ReadOnlySpan<byte> bytes, int position)
    {
        if (position <= 0 || position >= bytes.Length)
        {
            return true;
        }

        int firstCandidate = Math.Max(0, position - 3);
        for (int index = firstCandidate; index < position; index++)
        {
            if (RegexByteClass.TryGetUtf8ScalarLength(bytes, index, out int length) &&
                length > 1 &&
                position < index + length)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReferenceIsRegexWordBefore(ReadOnlySpan<byte> haystack, int position)
    {
        if (position <= 0)
        {
            return false;
        }

        int firstCandidate = Math.Max(0, position - 4);
        for (int index = firstCandidate; index < position; index++)
        {
            if (TryDecodeUtf8Scalar(haystack, index, out Rune rune, out int length) &&
                index + length == position)
            {
                return RegexUnicodeTables.IsPerlWord(rune);
            }
        }

        return false;
    }

    private static bool ReferenceIsRegexWordAt(ReadOnlySpan<byte> haystack, int position)
    {
        return position < haystack.Length &&
            TryDecodeUtf8Scalar(haystack, position, out Rune rune, out _) &&
            RegexUnicodeTables.IsPerlWord(rune);
    }

    private static bool ReferenceIsRegexWordContextValid(
        ReadOnlySpan<byte> haystack,
        int position)
    {
        return ReferenceIsValidRegexWordContextBefore(haystack, position) &&
            (position >= haystack.Length ||
                TryDecodeUtf8Scalar(haystack, position, out _, out _));
    }

    private static bool ReferenceIsValidRegexWordContextBefore(
        ReadOnlySpan<byte> haystack,
        int position)
    {
        if (position <= 0)
        {
            return true;
        }

        int firstCandidate = Math.Max(0, position - 4);
        for (int index = firstCandidate; index < position; index++)
        {
            if (TryDecodeUtf8Scalar(haystack, index, out _, out int length) &&
                index + length == position)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeUtf8Scalar(
        ReadOnlySpan<byte> bytes,
        int position,
        out Rune rune,
        out int length)
    {
        return Rune.DecodeFromUtf8(bytes[position..], out rune, out length) == OperationStatus.Done;
    }
}
