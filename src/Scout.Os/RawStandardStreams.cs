using System;

namespace Scout;

/// <summary>
/// Provides raw byte writers for the process standard streams.
/// </summary>
public static class RawStandardStreams
{
    /// <summary>
    /// Opens a raw byte writer for stdout.
    /// </summary>
    /// <returns>A raw byte writer over stdout.</returns>
    public static RawByteWriter OpenOutput()
    {
        return new RawByteWriter(Console.OpenStandardOutput());
    }

    /// <summary>
    /// Opens a raw byte writer for stderr.
    /// </summary>
    /// <returns>A raw byte writer over stderr.</returns>
    public static RawByteWriter OpenError()
    {
        return new RawByteWriter(Console.OpenStandardError());
    }
}
