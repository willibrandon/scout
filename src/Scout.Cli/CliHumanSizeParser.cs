using System;

namespace Scout;

/// <summary>
/// Parses human-readable byte sizes with ripgrep CLI semantics.
/// </summary>
public static class CliHumanSizeParser
{
    /// <summary>
    /// Parses a byte size with an optional uppercase binary suffix.
    /// </summary>
    /// <param name="text">The size text.</param>
    /// <param name="size">The parsed byte count.</param>
    /// <param name="error">The upstream-compatible error message when parsing fails.</param>
    /// <returns><see langword="true" /> when <paramref name="text" /> was parsed successfully.</returns>
    public static bool TryParse(string text, out ulong size, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);

        int digitsEnd = 0;
        while (digitsEnd < text.Length && text[digitsEnd] is >= '0' and <= '9')
        {
            digitsEnd++;
        }

        if (digitsEnd == 0)
        {
            return InvalidFormat(text, out size, out error);
        }

        ulong parsed = 0;
        for (int index = 0; index < digitsEnd; index++)
        {
            ulong digit = (ulong)(text[index] - '0');
            if (parsed > (ulong.MaxValue - digit) / 10)
            {
                size = 0;
                error = $"invalid integer found in size '{text}': number too large to fit in target type";
                return false;
            }

            parsed = (parsed * 10) + digit;
        }

        ReadOnlySpan<char> suffix = text.AsSpan(digitsEnd);
        ulong multiplier;
        if (suffix.IsEmpty)
        {
            multiplier = 1;
        }
        else if (suffix is "K")
        {
            multiplier = 1UL << 10;
        }
        else if (suffix is "M")
        {
            multiplier = 1UL << 20;
        }
        else if (suffix is "G")
        {
            multiplier = 1UL << 30;
        }
        else
        {
            return InvalidFormat(text, out size, out error);
        }

        if (parsed > ulong.MaxValue / multiplier)
        {
            size = 0;
            error = $"size too big in '{text}'";
            return false;
        }

        size = parsed * multiplier;
        error = string.Empty;
        return true;
    }

    private static bool InvalidFormat(string original, out ulong size, out string error)
    {
        size = 0;
        error = $"invalid format for size '{original}', which should be a non-empty sequence of digits followed by an optional 'K', 'M' or 'G' suffix";
        return false;
    }
}
