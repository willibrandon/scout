using System.Buffers;
using System.Text.Unicode;

namespace Scout;

/// <summary>
/// Writes UTF-8 output to a Windows console as UTF-16 code units.
/// </summary>
/// <remarks>
/// A console handle decodes <c>WriteFile</c> bytes with its active code page, so UTF-8 paths and lines
/// come out garbled unless that code page is UTF-8. Rust's standard library, and therefore ripgrep,
/// transcodes to UTF-16 and calls <c>WriteConsoleW</c> instead. This mirrors that boundary, including
/// holding a multi-byte sequence split across writes and rejecting bytes that are not UTF-8.
/// </remarks>
internal sealed class WindowsConsoleWriter
{
    // conhost serves console writes from a 64 KiB shared heap, so Rust caps one write at 8 KiB. That is
    // 4096 UTF-16 code units, and a UTF-8 sequence never needs more code units than it has bytes.
    private const int MaxUnitsPerWrite = 4096;
    private const int MaxUtf8SequenceLength = 4;
    private const string NonUtf8Message = "Windows stdio in console mode does not support writing non-UTF-8 byte sequences";

    private readonly Func<ReadOnlySpan<char>, int> _writeUnits;
    private readonly byte[] _incompleteUtf8 = new byte[MaxUtf8SequenceLength];
    private readonly char[] _units = new char[MaxUnitsPerWrite];
    private int _incompleteUtf8Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsConsoleWriter" /> class.
    /// </summary>
    /// <param name="writeUnits">Writes UTF-16 code units to the console and returns how many it accepted.</param>
    public WindowsConsoleWriter(Func<ReadOnlySpan<char>, int> writeUnits)
    {
        ArgumentNullException.ThrowIfNull(writeUnits);
        _writeUnits = writeUnits;
    }

    /// <summary>
    /// Writes the leading part of <paramref name="data" /> to the console.
    /// </summary>
    /// <param name="data">The UTF-8 bytes to write.</param>
    /// <returns>The number of bytes consumed, which the caller treats like a partial stream write.</returns>
    /// <exception cref="IOException">The bytes are not UTF-8, or the console rejected the write.</exception>
    public int Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        if (_incompleteUtf8Length > 0)
        {
            return ContinueIncompleteSequence(data[0]);
        }

        int length = Math.Min(data.Length, MaxUnitsPerWrite);
        OperationStatus status = Utf8.ToUtf16(data[..length], _units, out int bytesRead, out int unitCount, replaceInvalidSequences: false, isFinalBlock: false);
        if (bytesRead > 0)
        {
            return WriteUnits(bytesRead, unitCount);
        }

        // Nothing decoded: either the data is only the start of one multi-byte sequence, whose first
        // byte is held until the rest arrives one byte at a time, or it starts with bytes that are not
        // UTF-8.
        if (status != OperationStatus.NeedMoreData)
        {
            throw new IOException(NonUtf8Message);
        }

        _incompleteUtf8[0] = data[0];
        _incompleteUtf8Length = 1;
        return 1;
    }

    private int ContinueIncompleteSequence(byte value)
    {
        _incompleteUtf8[_incompleteUtf8Length++] = value;
        OperationStatus status = Utf8.ToUtf16(_incompleteUtf8.AsSpan(0, _incompleteUtf8Length), _units, out _, out int unitCount, replaceInvalidSequences: false, isFinalBlock: false);
        if (status == OperationStatus.NeedMoreData)
        {
            return 1;
        }

        _incompleteUtf8Length = 0;
        if (status != OperationStatus.Done)
        {
            throw new IOException(NonUtf8Message);
        }

        ReadOnlySpan<char> units = _units.AsSpan(0, unitCount);
        while (!units.IsEmpty)
        {
            int written = _writeUnits(units);
            if (written <= 0)
            {
                throw new IOException("standard stream write failed");
            }

            units = units[written..];
        }

        return 1;
    }

    private int WriteUnits(int byteCount, int unitCount)
    {
        ReadOnlySpan<char> units = _units.AsSpan(0, unitCount);
        int written = _writeUnits(units);
        if (written >= unitCount)
        {
            return byteCount;
        }

        // The console stopped part way through. Never leave the second half of a surrogate pair behind,
        // then report how many UTF-8 bytes the accepted code units covered.
        if (char.IsLowSurrogate(units[written]))
        {
            _ = _writeUnits(units.Slice(written, 1));
            written++;
        }

        int count = 0;
        for (int index = 0; index < written; index++)
        {
            count += units[index] switch
            {
                <= '\u007f' => 1,
                <= '߿' => 2,
                >= '\udc00' and <= '\udfff' => 1,
                _ => 3,
            };
        }

        return count;
    }
}
