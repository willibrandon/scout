using System.Buffers;

namespace Scout;

/// <summary>
/// Reads byte streams and applies Scout search-input transcoding.
/// </summary>
public static class SearchEncodingReader
{
    private const int BufferSize = 81920;

    /// <summary>
    /// Reads a stream to completion and decodes it according to the requested search encoding.
    /// </summary>
    /// <param name="stream">The source byte stream.</param>
    /// <param name="encodingKind">The requested search-input encoding.</param>
    /// <returns>The raw or transcoded bytes used by the searcher.</returns>
    public static byte[] ReadToEnd(Stream stream, SearchEncodingKind encodingKind)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using MemoryStream buffer = new();
        TranscodeTo(stream, buffer, encodingKind);
        return buffer.ToArray();
    }

    /// <summary>
    /// Streams a source to a destination while applying search-input transcoding.
    /// </summary>
    /// <param name="stream">The source byte stream.</param>
    /// <param name="destination">The destination for raw or transcoded bytes.</param>
    /// <param name="encodingKind">The requested search-input encoding.</param>
    public static void TranscodeTo(Stream stream, Stream destination, SearchEncodingKind encodingKind)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(destination);

        if (encodingKind == SearchEncodingKind.None)
        {
            CopyRaw(stream, destination);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(BufferSize);
        byte[] pending = Array.Empty<byte>();
        bool resolvedInitialEncoding = false;
        SearchEncodingKind effectiveEncodingKind = encodingKind;
        Iso2022JpDecoderState iso2022JpDecoderState = default;
        try
        {
            while (true)
            {
                int read = stream.Read(rented, 0, rented.Length);
                if (read == 0)
                {
                    break;
                }

                byte[]? combined = null;
                ReadOnlySpan<byte> buffer = rented.AsSpan(0, read);
                if (pending.Length != 0)
                {
                    combined = Combine(pending, buffer);
                    buffer = combined;
                }

                pending = ProcessBuffer(
                    buffer,
                    destination,
                    encodingKind,
                    ref resolvedInitialEncoding,
                    ref effectiveEncodingKind,
                    ref iso2022JpDecoderState,
                    flush: false);
            }

            pending = ProcessBuffer(
                pending,
                destination,
                encodingKind,
                ref resolvedInitialEncoding,
                ref effectiveEncodingKind,
                ref iso2022JpDecoderState,
                flush: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void CopyRaw(Stream stream, Stream destination)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                int read = stream.Read(rented, 0, rented.Length);
                if (read == 0)
                {
                    break;
                }

                destination.Write(rented.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static byte[] ProcessBuffer(
        ReadOnlySpan<byte> buffer,
        Stream destination,
        SearchEncodingKind requestedEncodingKind,
        ref bool resolvedInitialEncoding,
        ref SearchEncodingKind effectiveEncodingKind,
        ref Iso2022JpDecoderState iso2022JpDecoderState,
        bool flush)
    {
        if (!resolvedInitialEncoding)
        {
            if (!flush && buffer.Length < 3)
            {
                return buffer.ToArray();
            }

            int bomLength = ResolveInitialEncoding(buffer, requestedEncodingKind, out effectiveEncodingKind);
            buffer = buffer[bomLength..];
            resolvedInitialEncoding = true;
        }

        if (buffer.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        if (effectiveEncodingKind is SearchEncodingKind.None or SearchEncodingKind.Auto)
        {
            destination.Write(buffer);
            return Array.Empty<byte>();
        }

        if (effectiveEncodingKind == SearchEncodingKind.Iso2022Jp)
        {
            byte[] decoded = SearchEncoding.DecodeIso2022JpSegment(buffer, ref iso2022JpDecoderState, flush);
            destination.Write(decoded);
            return Array.Empty<byte>();
        }

        int safeLength = flush
            ? buffer.Length
            : SearchEncoding.GetStreamingSafePrefixLength(buffer, effectiveEncodingKind);
        if (safeLength != 0)
        {
            byte[] decoded = SearchEncoding.DecodeWithoutBom(buffer[..safeLength], effectiveEncodingKind);
            destination.Write(decoded);
        }

        return safeLength == buffer.Length
            ? Array.Empty<byte>()
            : buffer[safeLength..].ToArray();
    }

    private static int ResolveInitialEncoding(
        ReadOnlySpan<byte> buffer,
        SearchEncodingKind requestedEncodingKind,
        out SearchEncodingKind effectiveEncodingKind)
    {
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            effectiveEncodingKind = requestedEncodingKind == SearchEncodingKind.Utf8
                ? SearchEncodingKind.Utf8
                : SearchEncodingKind.None;
            return 3;
        }

        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
        {
            effectiveEncodingKind = SearchEncodingKind.Utf16Le;
            return 2;
        }

        if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
        {
            effectiveEncodingKind = SearchEncodingKind.Utf16Be;
            return 2;
        }

        effectiveEncodingKind = requestedEncodingKind == SearchEncodingKind.Auto
            ? SearchEncodingKind.None
            : requestedEncodingKind;
        return 0;
    }

    private static byte[] Combine(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        byte[] combined = new byte[first.Length + second.Length];
        first.CopyTo(combined);
        second.CopyTo(combined.AsSpan(first.Length));
        return combined;
    }
}
