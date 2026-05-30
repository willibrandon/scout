using System;
using System.IO;

namespace Scout;

internal static class PathUtil
{
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
}
