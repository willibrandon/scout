using System.Runtime.InteropServices;

namespace Scout;

internal unsafe sealed class NativeByteBuffer : IDisposable
{
    private void* _pointer;

    internal NativeByteBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        Length = length;
        _pointer = NativeMemory.Alloc((nuint)length);
        if (_pointer == null)
        {
            throw new InvalidOperationException("Native memory allocation failed.");
        }
    }

    ~NativeByteBuffer()
    {
        Free();
    }

    internal int Length { get; }

    internal Span<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_pointer == null, this);
            return new Span<byte>(_pointer, Length);
        }
    }

    public void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    private void Free()
    {
        void* pointer = _pointer;
        if (pointer == null)
        {
            return;
        }

        _pointer = null;
        NativeMemory.Free(pointer);
    }
}
