using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace Scout;

/// <summary>
/// Owns a read-only file mapping and exposes one zero-copy view at a time.
/// </summary>
/// <param name="path">The file path.</param>
/// <param name="length">The mapped file length.</param>
/// <param name="logger">The logger for nonfatal mapping advice failures.</param>
/// <param name="advise">An optional native advice override for deterministic failure testing.</param>
internal sealed unsafe class MemoryMappedSearchFile(
    string path,
    long length,
    DiagnosticLogger logger = default,
    Func<nint, nuint, int>? advise = null) : IDisposable
{
    private MemoryMappedFile? _mappedFile = CreateMappedFile(path);
    private MemoryMappedViewAccessor? _accessor;
    private byte* _pointer;
    private int _viewLength;
    private readonly long _length = length;

    /// <summary>
    /// Releases the view and mapping when deterministic disposal was missed.
    /// </summary>
    ~MemoryMappedSearchFile()
    {
        ReleaseResources();
    }

    /// <summary>
    /// Gets the current view bytes. The returned span is valid until the view changes or this
    /// instance is disposed.
    /// </summary>
    public ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_mappedFile is null, this);
            if (_pointer is null)
            {
                throw new InvalidOperationException("No memory-mapped view is active.");
            }

            return new ReadOnlySpan<byte>(_pointer, _viewLength);
        }
    }

    /// <summary>
    /// Gets the mapped file length.
    /// </summary>
    public long Length => _length;

    /// <summary>
    /// Attempts to open a non-empty file as one zero-copy read-only view.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="mappedSearchFile">Receives the mapped file.</param>
    /// <returns><see langword="true" /> when the complete file was mapped successfully.</returns>
    public static bool TryOpen(string path, out MemoryMappedSearchFile? mappedSearchFile)
    {
        if (!TryOpenFile(path, out mappedSearchFile))
        {
            return false;
        }

        if (mappedSearchFile!.Length > int.MaxValue ||
            !mappedSearchFile.TryMapView(offset: 0, checked((int)mappedSearchFile.Length)))
        {
            mappedSearchFile.Dispose();
            mappedSearchFile = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to open a non-empty file for one or more bounded read-only views.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="mappedSearchFile">Receives the mapped file without an initial view.</param>
    /// <param name="logger">The logger for nonfatal mapping advice failures.</param>
    /// <param name="advise">An optional native advice override for deterministic failure testing.</param>
    /// <returns><see langword="true" /> when the file mapping was opened successfully.</returns>
    public static bool TryOpenFile(string path, out MemoryMappedSearchFile? mappedSearchFile, DiagnosticLogger logger = default,
        Func<nint, nuint, int>? advise = null)
    {
        mappedSearchFile = null;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.SequentialScan);
            long length = stream.Length;
            if (length <= 0)
            {
                return false;
            }

            stream.Dispose();
            mappedSearchFile = new MemoryMappedSearchFile(path, length, logger, advise);
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

    /// <summary>
    /// Replaces the current view with a bounded view starting at one file offset.
    /// </summary>
    /// <param name="offset">The zero-based file offset.</param>
    /// <param name="maximumLength">The maximum number of bytes to expose.</param>
    /// <returns><see langword="true" /> when the requested view was mapped successfully.</returns>
    public bool TryMapView(long offset, int maximumLength)
    {
        ObjectDisposedException.ThrowIf(_mappedFile is null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        if (offset >= _length)
        {
            return false;
        }

        ReleaseView();
        using var accessorOwner = new DisposableOwner<MemoryMappedViewAccessor>();
        SafeMemoryMappedViewHandle? handle = null;
        byte* pointer = null;
        try
        {
            int viewLength = checked((int)Math.Min(maximumLength, _length - offset));
            accessorOwner.Resource = _mappedFile.CreateViewAccessor(
                offset,
                viewLength,
                MemoryMappedFileAccess.Read);
            handle = accessorOwner.Resource.SafeMemoryMappedViewHandle;
            handle.AcquirePointer(ref pointer);
            if (!NativeMemoryAdvice.TrySequential(pointer, checked((nuint)handle.ByteLength), out int error, advise))
            {
                logger.Debug("scout::searcher", "src/Scout.App/MemoryMappedSearchFile.cs",
                    $"{path}: madvise failed: {new System.ComponentModel.Win32Exception(error).Message} (os error {error})");
            }

            pointer += accessorOwner.Resource.PointerOffset;

            _accessor = accessorOwner.Resource;
            _pointer = pointer;
            _viewLength = viewLength;
            accessorOwner.Release();
            handle = null;
            pointer = null;
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
        finally
        {
            if (pointer is not null)
            {
                handle!.ReleasePointer();
            }
        }
    }

    /// <summary>
    /// Releases the current view and its underlying file mapping.
    /// </summary>
    public void Dispose()
    {
        ReleaseResources();
        GC.SuppressFinalize(this);
    }

    private void ReleaseResources()
    {
        ReleaseView();
        _mappedFile?.Dispose();
        _mappedFile = null;
    }

    private static MemoryMappedFile CreateMappedFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.SequentialScan);
        return MemoryMappedFile.CreateFromFile(
            stream,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read,
            HandleInheritability.None,
            leaveOpen: true);
    }

    private void ReleaseView()
    {
        if (_pointer is null)
        {
            return;
        }

        _pointer = null;
        _viewLength = 0;
        _accessor!.SafeMemoryMappedViewHandle.ReleasePointer();
        _accessor.Dispose();
        _accessor = null;
    }
}
