using System.Text;

namespace Scout;

internal readonly struct SearchPathArgument
{
    private static readonly UTF8Encoding Utf8Lossy = new(encoderShouldEmitUTF8Identifier: false);

    private SearchPathArgument(string? text, byte[]? unixBytes, byte[] displayBytes)
    {
        Text = text;
        UnixBytes = unixBytes;
        DisplayBytes = displayBytes;
    }

    public string? Text { get; }

    public byte[]? UnixBytes { get; }

    public byte[] DisplayBytes { get; }

    public bool IsRawUnixPath => UnixBytes is not null;

    public string DisplayText => Text ?? Utf8Lossy.GetString(DisplayBytes);

    public static SearchPathArgument CreateText(string path)
    {
        return FromText(path, GetPathBytes(path, pathSeparator: null));
    }

    public static SearchPathArgument FromText(string text, byte[] displayBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(displayBytes);
        return new SearchPathArgument(text, unixBytes: null, displayBytes);
    }

    public static SearchPathArgument FromUnixBytes(ReadOnlySpan<byte> unixBytes, byte[] displayBytes)
    {
        ArgumentNullException.ThrowIfNull(displayBytes);
        return new SearchPathArgument(text: null, unixBytes.ToArray(), displayBytes);
    }

    public static bool TryGetText(OsString argument, DiagnosticMessenger diagnostics, out string path)
    {
        if (argument.TryGetText(out path))
        {
            return true;
        }

        diagnostics.ErrorMessage(new ScoutError("invalid CLI arguments").WithContext("rg"));
        return false;
    }

    public static bool TryCreate(
        OsString argument,
        byte? pathSeparator,
        DiagnosticMessenger diagnostics,
        out SearchPathArgument path)
    {
        if (argument.TryGetText(out string text))
        {
            path = FromText(text, GetPathBytes(text, pathSeparator));
            return true;
        }

        if (argument.IsUnixBytes && !OperatingSystem.IsWindows())
        {
            ReadOnlySpan<byte> bytes = argument.AsUnixBytes();
            if (bytes.IndexOf((byte)0) >= 0)
            {
                diagnostics.ErrorMessage(new ScoutError("invalid CLI arguments").WithContext("rg"));
                path = default;
                return false;
            }

            path = FromUnixBytes(bytes, GetPathBytes(bytes, pathSeparator));
            return true;
        }

        diagnostics.ErrorMessage(new ScoutError("invalid CLI arguments").WithContext("rg"));
        path = default;
        return false;
    }

    public static bool ContainsDirectory(IReadOnlyList<SearchPathArgument> paths)
    {
        for (int index = 0; index < paths.Count; index++)
        {
            string? path = paths[index].Text;
            if (path is not null && path != "-" && Directory.Exists(path))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAutoMmapEligible(IReadOnlyList<SearchPathArgument> paths)
    {
        if (paths.Count == 0 || paths.Count > 10)
        {
            return false;
        }

        for (int index = 0; index < paths.Count; index++)
        {
            string? path = paths[index].Text;
            if (path is null || !File.Exists(path))
            {
                return false;
            }
        }

        return true;
    }

    public static byte[] GetPathBytes(string path, byte? pathSeparator)
    {
        byte[] bytes = Utf8Lossy.GetBytes(path);
        ApplyPathSeparator(bytes, pathSeparator);
        return bytes;
    }

    public static byte[] GetPathBytes(ReadOnlySpan<byte> path, byte? pathSeparator)
    {
        byte[] bytes = path.ToArray();
        ApplyPathSeparator(bytes, pathSeparator);
        return bytes;
    }

    public static string GetDirectoryDisplayPath(string rootArgument, string fullRoot, string fullPath)
    {
        string relative = Path.GetRelativePath(fullRoot, fullPath);
        string root = Path.TrimEndingDirectorySeparator(rootArgument);
        if (root.Length == 0)
        {
            root = rootArgument;
        }

        if (root == ".")
        {
            return "." + Path.DirectorySeparatorChar + relative;
        }

        return Path.Combine(root, relative);
    }

    public static string GetSearchDirectoryDisplayPath(string rootArgument, string fullRoot, string fullPath, bool defaultRoot)
    {
        if (defaultRoot)
        {
            return Path.GetRelativePath(fullRoot, fullPath);
        }

        return GetDirectoryDisplayPath(rootArgument, fullRoot, fullPath);
    }

    public static byte[] GetSearchDirectoryDisplayPathBytes(
        string rootArgument,
        string fullRoot,
        DirEntry entry,
        bool defaultRoot,
        byte? pathSeparator)
    {
        return CreateDirectoryDisplayPathFormatter(rootArgument, fullRoot, defaultRoot, pathSeparator).GetBytes(entry);
    }

    public static SearchDirectoryDisplayPathFormatter CreateDirectoryDisplayPathFormatter(
        string rootArgument,
        string fullRoot,
        bool defaultRoot,
        byte? pathSeparator)
    {
        return new SearchDirectoryDisplayPathFormatter(rootArgument, fullRoot, defaultRoot, pathSeparator);
    }

    internal static byte[] GetSearchDirectoryDisplayPathBytesUncached(
        string rootArgument,
        string fullRoot,
        DirEntry entry,
        bool defaultRoot,
        byte? pathSeparator)
    {
        if (entry.IsRawUnixPath && TryGetRawUnixRelativePath(fullRoot, entry, out byte[] relativePath))
        {
            byte[] displayPath = defaultRoot
                ? relativePath
                : CombineRawDisplayPath(rootArgument, relativePath);
            ApplyPathSeparator(displayPath, pathSeparator);
            return displayPath;
        }

        return GetPathBytes(GetSearchDirectoryDisplayPath(rootArgument, fullRoot, entry.FullPath, defaultRoot), pathSeparator);
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

    private static bool TryGetRawUnixRelativePath(string fullRoot, DirEntry entry, out byte[] relativePath)
    {
        byte[] rootBytes = Utf8Lossy.GetBytes(Path.TrimEndingDirectorySeparator(fullRoot));
        ReadOnlySpan<byte> path = entry.UnixPathBytes;
        if (!path.StartsWith(rootBytes))
        {
            relativePath = [];
            return false;
        }

        int offset = rootBytes.Length;
        if (offset < path.Length && path[offset] == (byte)'/')
        {
            offset++;
        }

        relativePath = path[offset..].ToArray();
        return relativePath.Length > 0;
    }

    private static byte[] GetPathBytes(ReadOnlySpan<char> path, byte? pathSeparator)
    {
        byte[] bytes = new byte[Utf8Lossy.GetByteCount(path)];
        Utf8Lossy.GetBytes(path, bytes);
        ApplyPathSeparator(bytes, pathSeparator);
        return bytes;
    }

    private static byte[] CombineRawDisplayPath(string rootArgument, ReadOnlySpan<byte> relativePath)
    {
        string root = Path.TrimEndingDirectorySeparator(rootArgument);
        if (root.Length == 0)
        {
            root = rootArgument;
        }

        byte[] rootBytes = Utf8Lossy.GetBytes(root);
        bool needsSeparator = rootBytes.Length != 1 || rootBytes[0] != (byte)'/';
        byte[] displayPath = new byte[rootBytes.Length + (needsSeparator ? 1 : 0) + relativePath.Length];
        rootBytes.CopyTo(displayPath);
        int offset = rootBytes.Length;
        if (needsSeparator)
        {
            displayPath[offset] = (byte)'/';
            offset++;
        }

        relativePath.CopyTo(displayPath.AsSpan(offset));
        return displayPath;
    }
}
