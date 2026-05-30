using System;

namespace Scout;

internal static unsafe class NativeArgumentReader
{
    internal static OsString[] CaptureUnix(int argc, byte** argv)
    {
        if (argc < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(argc), argc, "Argument count cannot be negative.");
        }

        if (argv is null && argc != 0)
        {
            throw new ArgumentNullException(nameof(argv));
        }

        var arguments = new OsString[argc];
        for (int index = 0; index < argc; index++)
        {
            byte* pointer = argv[index];
            if (pointer is null)
            {
                throw new ArgumentException("Argument pointers cannot contain null entries.", nameof(argv));
            }

            int length = MeasureNullTerminated(pointer);
            arguments[index] = OsString.FromUnixBytes(new ReadOnlySpan<byte>(pointer, length));
        }

        return arguments;
    }

    private static int MeasureNullTerminated(byte* pointer)
    {
        nuint length = 0;
        while (pointer[length] != 0)
        {
            length++;
            if (length > int.MaxValue)
            {
                throw new ArgumentException("Argument is too large to address as a managed span.", nameof(pointer));
            }
        }

        return checked((int)length);
    }
}
