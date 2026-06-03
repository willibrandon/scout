
namespace Scout;

/// <summary>
/// Represents a prepared byte path for repeated glob matching.
/// </summary>
public sealed class GlobCandidate
{
    private readonly byte[] path;
    private readonly byte[] baseName;
    private readonly byte[] extension;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobCandidate" /> class.
    /// </summary>
    /// <param name="path">The path bytes to prepare.</param>
    public GlobCandidate(byte[] path)
        : this(path.AsSpan())
    {
    }

    private GlobCandidate(ReadOnlySpan<byte> path)
    {
        this.path = NormalizePath(path);
        baseName = GetBaseName(this.path);
        extension = GetExtension(baseName);
    }

    /// <summary>
    /// Gets the normalized path bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Path => path;

    /// <summary>
    /// Gets the final path component bytes, or an empty value when no file name exists.
    /// </summary>
    public ReadOnlyMemory<byte> BaseName => baseName;

    /// <summary>
    /// Gets the final path component extension bytes, including the leading dot.
    /// </summary>
    public ReadOnlyMemory<byte> Extension => extension;

    /// <summary>
    /// Creates a candidate from path bytes.
    /// </summary>
    /// <param name="path">The path bytes to prepare.</param>
    /// <returns>A prepared glob candidate.</returns>
    public static GlobCandidate FromBytes(ReadOnlySpan<byte> path)
    {
        return new GlobCandidate(path);
    }

    private static byte[] NormalizePath(ReadOnlySpan<byte> path)
    {
        byte[] normalized = path.ToArray();
        if (!OperatingSystem.IsWindows())
        {
            return normalized;
        }

        for (int index = 0; index < normalized.Length; index++)
        {
            if (normalized[index] == (byte)'\\')
            {
                normalized[index] = (byte)'/';
            }
        }

        return normalized;
    }

    private static byte[] GetBaseName(ReadOnlySpan<byte> path)
    {
        if (path.IsEmpty)
        {
            return [];
        }

        int start = 0;
        for (int index = path.Length - 1; index >= 0; index--)
        {
            if (path[index] == (byte)'/')
            {
                start = index + 1;
                break;
            }
        }

        ReadOnlySpan<byte> name = path[start..];
        return name.SequenceEqual(".."u8) ? [] : name.ToArray();
    }

    private static byte[] GetExtension(ReadOnlySpan<byte> baseName)
    {
        if (baseName.IsEmpty)
        {
            return [];
        }

        for (int index = baseName.Length - 1; index >= 0; index--)
        {
            if (baseName[index] == (byte)'.')
            {
                return baseName[index..].ToArray();
            }
        }

        return [];
    }
}
