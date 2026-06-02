using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace Scout;

/// <summary>
/// Reads search files with ripgrep-compatible mmap selection.
/// </summary>
public static class SearchFileReader
{
    /// <summary>
    /// Reads a search file and decodes it according to the requested search encoding.
    /// </summary>
    /// <param name="path">The file path to read.</param>
    /// <param name="encodingKind">The requested search-input encoding.</param>
    /// <param name="mmapMode">The requested memory-map mode.</param>
    /// <returns>The decoded bytes and the strategy used to read them.</returns>
    public static SearchFileReadResult Read(
        string path,
        SearchEncodingKind encodingKind,
        SearchMmapMode mmapMode)
    {
        return Read(path, encodingKind, mmapMode, allowMemoryMap: true);
    }

    /// <summary>
    /// Reads a search file and decodes it according to the requested search encoding.
    /// </summary>
    /// <param name="path">The file path to read.</param>
    /// <param name="encodingKind">The requested search-input encoding.</param>
    /// <param name="mmapMode">The requested memory-map mode.</param>
    /// <param name="allowMemoryMap">Whether automatic mmap selection is allowed for this path.</param>
    /// <returns>The decoded bytes and the strategy used to read them.</returns>
    public static SearchFileReadResult Read(
        string path,
        SearchEncodingKind encodingKind,
        SearchMmapMode mmapMode,
        bool allowMemoryMap)
    {
        return Read(path, encodingKind, mmapMode, allowMemoryMap, knownLength: null);
    }

    /// <summary>
    /// Reads a search file and decodes it according to the requested search encoding.
    /// </summary>
    /// <param name="path">The file path to read.</param>
    /// <param name="encodingKind">The requested search-input encoding.</param>
    /// <param name="mmapMode">The requested memory-map mode.</param>
    /// <param name="allowMemoryMap">Whether automatic mmap selection is allowed for this path.</param>
    /// <param name="knownLength">The file length from directory metadata, when available.</param>
    /// <returns>The decoded bytes and the strategy used to read them.</returns>
    public static SearchFileReadResult Read(
        string path,
        SearchEncodingKind encodingKind,
        SearchMmapMode mmapMode,
        bool allowMemoryMap,
        long? knownLength)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (ShouldTryMemoryMap(mmapMode, allowMemoryMap) &&
            TryReadMemoryMapped(path, encodingKind, out byte[] mappedBytes))
        {
            return new SearchFileReadResult(mappedBytes, SearchFileReadKind.MemoryMapped);
        }

        return new SearchFileReadResult(ReadBuffered(path, encodingKind, knownLength), SearchFileReadKind.Buffered);
    }

    /// <summary>
    /// Reads a Unix search file whose path is available only as raw bytes.
    /// </summary>
    /// <param name="path">The raw Unix path bytes.</param>
    /// <param name="encodingKind">The requested search-input encoding.</param>
    /// <returns>The decoded bytes and the strategy used to read them.</returns>
    public static SearchFileReadResult ReadUnixPath(ReadOnlySpan<byte> path, SearchEncodingKind encodingKind)
    {
        using SafeFileHandle handle = RawUnixFile.OpenRead(path);
        using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 4096, isAsync: false);
        return new SearchFileReadResult(SearchEncodingReader.ReadToEnd(stream, encodingKind), SearchFileReadKind.Buffered);
    }

    private static bool ShouldTryMemoryMap(SearchMmapMode mmapMode, bool allowMemoryMap)
    {
        if (OperatingSystem.IsMacOS())
        {
            return false;
        }

        return mmapMode switch
        {
            SearchMmapMode.AlwaysTryMmap => true,
            SearchMmapMode.Auto => allowMemoryMap,
            _ => false,
        };
    }

    private static byte[] ReadBuffered(string path, SearchEncodingKind encodingKind, long? knownLength)
    {
        if (encodingKind is SearchEncodingKind.Auto or SearchEncodingKind.None)
        {
            return ReadRawBuffered(path, encodingKind, knownLength);
        }

        using FileStream stream = OpenReadStream(path, bufferSize: 4096);
        return SearchEncodingReader.ReadToEnd(stream, encodingKind);
    }

    private static byte[] ReadRawBuffered(string path, SearchEncodingKind encodingKind, long? knownLength)
    {
        using FileStream stream = OpenReadStream(path, bufferSize: 1);
        long length = knownLength ?? stream.Length;
        if (length > int.MaxValue)
        {
            throw new IOException("file is too large to search in memory");
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>(checked((int)length));
        stream.ReadExactly(bytes);
        return SearchEncoding.Decode(bytes, encodingKind);
    }

    private static FileStream OpenReadStream(string path, int bufferSize)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize,
            FileOptions.SequentialScan);
    }

    private static bool TryReadMemoryMapped(string path, SearchEncodingKind encodingKind, out byte[] bytes)
    {
        bytes = [];
        try
        {
            long length = new FileInfo(path).Length;
            if (length == 0)
            {
                return false;
            }

            if (length > int.MaxValue)
            {
                throw new IOException("file is too large to search in memory");
            }

            bytes = ReadMemoryMapped(path, length, encodingKind);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static unsafe byte[] ReadMemoryMapped(string path, long length, SearchEncodingKind encodingKind)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var mappedFile = MemoryMappedFile.CreateFromFile(
            stream,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read,
            HandleInheritability.None,
            leaveOpen: false);
        using MemoryMappedViewAccessor accessor = mappedFile.CreateViewAccessor(
            offset: 0,
            size: length,
            MemoryMappedFileAccess.Read);
        SafeMemoryMappedViewHandle handle = accessor.SafeMemoryMappedViewHandle;
        byte* pointer = null;
        handle.AcquirePointer(ref pointer);
        try
        {
            ReadOnlySpan<byte> mappedBytes = new(pointer + accessor.PointerOffset, checked((int)length));
            return SearchEncoding.Decode(mappedBytes, encodingKind);
        }
        finally
        {
            handle.ReleasePointer();
        }
    }
}
