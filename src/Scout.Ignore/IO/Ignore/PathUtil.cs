
namespace Scout.IO.Ignore;

internal static class PathUtil
{
    public static bool IsPathUnderBase(string baseDirectory, string path)
    {
        string baseFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        string pathFullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(baseFullPath, pathFullPath);
        return relative == "." || !IsRelativePathOutsideBase(relative);
    }

    public static bool IsRelativePathOutsideBase(string relative)
    {
        return Path.IsPathRooted(relative) ||
            relative == ".." ||
            (relative.Length > 2 && relative[0] == '.' && relative[1] == '.' && IsDirectorySeparator(relative[2]));
    }

    public static bool IsHidden(DirEntry entry)
    {
        if (entry.IsStdin)
        {
            return false;
        }

        if (entry.IsRawUnixPath)
        {
            ReadOnlySpan<byte> fileName = entry.UnixFileNameBytes;
            return !fileName.IsEmpty && fileName[0] == (byte)'.';
        }

        if (entry.FileName.StartsWith('.'))
        {
            return true;
        }

        return OperatingSystem.IsWindows() && (entry.Attributes & FileAttributes.Hidden) != 0;
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
    }
}
