using System.Text;

namespace Scout;

internal readonly struct SearchDirectoryDisplayPathFormatter
{
    private static readonly UTF8Encoding Utf8Lossy = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string rootArgument;
    private readonly string fullRoot;
    private readonly string rootPath;
    private readonly byte[]? displayPrefix;
    private readonly bool defaultRoot;
    private readonly byte? pathSeparator;
    private readonly StringComparison comparison;

    public SearchDirectoryDisplayPathFormatter(string rootArgument, string fullRoot, bool defaultRoot, byte? pathSeparator)
    {
        this.rootArgument = rootArgument;
        this.fullRoot = fullRoot;
        rootPath = Path.TrimEndingDirectorySeparator(fullRoot);
        displayPrefix = defaultRoot ? null : GetDisplayRootPrefixBytes(rootArgument, pathSeparator);
        this.defaultRoot = defaultRoot;
        this.pathSeparator = pathSeparator;
        comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public byte[] GetBytes(DirEntry entry)
    {
        if (entry.IsRawUnixPath)
        {
            return SearchPathArgument.GetSearchDirectoryDisplayPathBytesUncached(
                rootArgument,
                fullRoot,
                entry,
                defaultRoot,
                pathSeparator);
        }

        if (TryGetDisplayPathBytesFromFullPath(entry.FullPath, out byte[] displayBytes))
        {
            return displayBytes;
        }

        return SearchPathArgument.GetSearchDirectoryDisplayPathBytesUncached(
            rootArgument,
            fullRoot,
            entry,
            defaultRoot,
            pathSeparator);
    }

    private bool TryGetDisplayPathBytesFromFullPath(string fullPath, out byte[] displayPath)
    {
        if (rootPath.Length == 0 ||
            fullPath.Length <= rootPath.Length ||
            !fullPath.StartsWith(rootPath, comparison) ||
            !IsDirectorySeparator(fullPath[rootPath.Length]))
        {
            displayPath = [];
            return false;
        }

        ReadOnlySpan<char> relativePath = fullPath.AsSpan(rootPath.Length + 1);
        displayPath = defaultRoot
            ? GetPathBytes(relativePath, pathSeparator)
            : CombineDisplayPath(displayPrefix!, relativePath, pathSeparator);
        return displayPath.Length > 0;
    }

    private static byte[] GetDisplayRootPrefixBytes(string rootArgument, byte? pathSeparator)
    {
        string root = Path.TrimEndingDirectorySeparator(rootArgument);
        if (root.Length == 0)
        {
            root = rootArgument;
        }

        ReadOnlySpan<char> prefix = root;
        bool hasTrailingSeparator = root != "." && Path.EndsInDirectorySeparator(root);
        int separatorLength = hasTrailingSeparator ? 0 : 1;
        int byteCount = Utf8Lossy.GetByteCount(prefix) + separatorLength;
        byte[] displayPrefix = new byte[byteCount];
        int offset = Utf8Lossy.GetBytes(prefix, displayPrefix);
        if (separatorLength != 0)
        {
            displayPrefix[offset] = pathSeparator ?? (byte)Path.DirectorySeparatorChar;
        }

        ApplyPathSeparator(displayPrefix, pathSeparator);
        return displayPrefix;
    }

    private static byte[] CombineDisplayPath(byte[] displayPrefix, ReadOnlySpan<char> relativePath, byte? pathSeparator)
    {
        int relativeByteCount = Utf8Lossy.GetByteCount(relativePath);
        byte[] displayPath = new byte[displayPrefix.Length + relativeByteCount];
        displayPrefix.CopyTo(displayPath, 0);
        Utf8Lossy.GetBytes(relativePath, displayPath.AsSpan(displayPrefix.Length));
        ApplyPathSeparator(displayPath, pathSeparator);
        return displayPath;
    }

    private static byte[] GetPathBytes(ReadOnlySpan<char> path, byte? pathSeparator)
    {
        byte[] bytes = new byte[Utf8Lossy.GetByteCount(path)];
        Utf8Lossy.GetBytes(path, bytes);
        ApplyPathSeparator(bytes, pathSeparator);
        return bytes;
    }

    private static void ApplyPathSeparator(byte[] bytes, byte? pathSeparator)
    {
        if (pathSeparator is not byte separator)
        {
            return;
        }

        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'/' || (OperatingSystem.IsWindows() && bytes[index] == (byte)'\\'))
            {
                bytes[index] = separator;
            }
        }
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
    }
}
