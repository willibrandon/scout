
namespace Scout;

/// <summary>
/// Verifies native argv capture behavior.
/// </summary>
public sealed unsafe class NativeArgumentReaderTests
{
    /// <summary>
    /// Verifies Unix argv capture preserves non-UTF-8 argument bytes.
    /// </summary>
    [Fact]
    public void CaptureUnixPreservesRawArgumentBytes()
    {
        byte[] executable = [0x73, 0x63, 0x6f, 0x75, 0x74, 0x00];
        byte[] flag = [0x2d, 0x56, 0x00];
        byte[] invalid = [0xff, 0x80, 0x00];

        fixed (byte* executablePointer = executable)
        fixed (byte* flagPointer = flag)
        fixed (byte* invalidPointer = invalid)
        {
            byte** argv = stackalloc byte*[3];
            argv[0] = executablePointer;
            argv[1] = flagPointer;
            argv[2] = invalidPointer;

            OsString[] arguments = NativeArgumentReader.CaptureUnix(3, argv);

            Assert.Equal(3, arguments.Length);
            Assert.Equal([0x73, 0x63, 0x6f, 0x75, 0x74], arguments[0].AsUnixBytes().ToArray());
            Assert.Equal([0x2d, 0x56], arguments[1].AsUnixBytes().ToArray());
            Assert.Equal([0xff, 0x80], arguments[2].AsUnixBytes().ToArray());
        }
    }
}
