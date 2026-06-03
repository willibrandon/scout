using System.Text;

namespace Scout;

internal sealed class OutputPath
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] OpenHyperlink = "\u001b]8;;"u8.ToArray();
    private static readonly byte[] CloseHyperlink = "\u001b]8;;\u001b\\"u8.ToArray();
    private static readonly byte[] EndControl = "\u001b\\"u8.ToArray();

    public OutputPath(byte[] display, byte[]? hyperlinkPath, string? hyperlinkFormat, string host)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(host);
        Display = display;
        HyperlinkPath = hyperlinkPath;
        HyperlinkFormat = string.IsNullOrEmpty(hyperlinkFormat) ? null : hyperlinkFormat;
        Host = host;
    }

    public byte[] Display { get; }

    public bool HasHyperlink => HyperlinkPath is not null && HyperlinkFormat is not null;

    public bool HyperlinkIsLineDependent => HyperlinkFormat?.Contains("{line}", StringComparison.Ordinal) == true;

    private byte[]? HyperlinkPath { get; }

    private string? HyperlinkFormat { get; }

    private string Host { get; }

    public void WritePlain(RawByteWriter output)
    {
        output.Write(Display);
    }

    public void WriteLabel(RawByteWriter output, OutputColor color)
    {
        bool linked = BeginHyperlink(output, lineNumber: null, column: null);
        color.WritePath(output, Display);
        EndHyperlink(output, linked);
    }

    public bool BeginHyperlink(RawByteWriter output, long? lineNumber, long? column)
    {
        if (!HasHyperlink)
        {
            return false;
        }

        output.Write(OpenHyperlink);
        WriteInterpolatedUri(output, lineNumber, column);
        output.Write(EndControl);
        return true;
    }

    public static void EndHyperlink(RawByteWriter output, bool active)
    {
        if (active)
        {
            output.Write(CloseHyperlink);
        }
    }

    public static void EndHyperlink(RawByteWriter output, ref bool active)
    {
        if (!active)
        {
            return;
        }

        output.Write(CloseHyperlink);
        active = false;
    }

    public static byte[]? CreateHyperlinkPath(string path)
    {
        try
        {
            string fullPath = GetCanonicalPath(path);
            if (!Path.IsPathFullyQualified(fullPath))
            {
                return null;
            }

            if (OperatingSystem.IsWindows() && IsWindowsDrivePath(fullPath))
            {
                fullPath = "/" + fullPath;
            }

            return PercentEncodePath(Utf8.GetBytes(fullPath));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string GetCanonicalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            return fullPath;
        }

        string root = Path.GetPathRoot(fullPath) ?? "/";
        string current = root;
        string relative = fullPath[root.Length..];
        string[] segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < segments.Length; index++)
        {
            string candidate = Path.Combine(current, segments[index]);
            FileSystemInfo? resolved = ResolveLinkTarget(candidate);
            current = resolved?.FullName ?? candidate;
        }

        return current;
    }

    private static bool IsWindowsDrivePath(string path)
    {
        return path.Length >= 3 &&
            char.IsAsciiLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/');
    }

    private static FileSystemInfo? ResolveLinkTarget(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
        }

        if (File.Exists(path))
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
        }

        return null;
    }

    private void WriteInterpolatedUri(RawByteWriter output, long? lineNumber, long? column)
    {
        string format = HyperlinkFormat!;
        int position = 0;
        while (position < format.Length)
        {
            int open = format.IndexOf('{', position);
            if (open < 0)
            {
                WriteUtf8(output, format.AsSpan(position));
                return;
            }

            WriteUtf8(output, format.AsSpan(position, open - position));
            int close = format.IndexOf('}', open + 1);
            ReadOnlySpan<char> variable = format.AsSpan(open + 1, close - open - 1);
            if (variable.SequenceEqual("host"))
            {
                WriteUtf8(output, Host);
            }
            else if (variable.SequenceEqual("wslprefix"))
            {
                // Scout currently has no WSL path translation layer; an unset value interpolates as empty in ripgrep.
            }
            else if (variable.SequenceEqual("path"))
            {
                output.Write(HyperlinkPath!);
            }
            else if (variable.SequenceEqual("line"))
            {
                OutputColor.WriteNumber(output, lineNumber ?? 1);
            }
            else if (variable.SequenceEqual("column"))
            {
                OutputColor.WriteNumber(output, column ?? 1);
            }

            position = close + 1;
        }
    }

    private static void WriteUtf8(RawByteWriter output, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        byte[] bytes = Utf8.GetBytes(text.ToString());
        output.Write(bytes);
    }

    private static byte[] PercentEncodePath(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.Length);
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            if (IsUnreservedPathByte(value))
            {
                stream.WriteByte(value);
            }
            else if (OperatingSystem.IsWindows() && value == (byte)'\\')
            {
                stream.WriteByte((byte)'/');
            }
            else
            {
                stream.WriteByte((byte)'%');
                stream.WriteByte(ToHex(value >> 4));
                stream.WriteByte(ToHex(value & 0xF));
            }
        }

        return stream.ToArray();
    }

    private static bool IsUnreservedPathByte(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9' or
            >= (byte)'A' and <= (byte)'Z' or
            >= (byte)'a' and <= (byte)'z' or
            (byte)'/' or
            (byte)':' or
            (byte)'-' or
            (byte)'.' or
            (byte)'_' or
            (byte)'~' or
            >= 128;
    }

    private static byte ToHex(int value)
    {
        return (byte)(value < 10 ? value + (byte)'0' : value - 10 + (byte)'A');
    }
}
