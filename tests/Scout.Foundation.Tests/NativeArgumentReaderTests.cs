
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

    /// <summary>
    /// Verifies Windows argv capture preserves UTF-16 argument text.
    /// </summary>
    [Fact]
    public void CaptureWindowsWidePreservesArgumentText()
    {
        fixed (char* executablePointer = "scout\0")
        fixed (char* flagPointer = "-V\0")
        fixed (char* pathPointer = "C:\\tmp\\file.txt\0")
        {
            char** argv = stackalloc char*[3];
            argv[0] = executablePointer;
            argv[1] = flagPointer;
            argv[2] = pathPointer;

            OsString[] arguments = NativeArgumentReader.CaptureWindowsWide(3, argv);

            Assert.Equal(3, arguments.Length);
            Assert.Equal("scout", arguments[0].AsWindowsString());
            Assert.Equal("-V", arguments[1].AsWindowsString());
            Assert.Equal("C:\\tmp\\file.txt", arguments[2].AsWindowsString());
        }
    }

    /// <summary>
    /// Verifies Unix environment capture preserves raw entries and resolves UTF-8 values without lossy fallback.
    /// </summary>
    [Fact]
    public void CaptureUnixEnvironmentPreservesRawEntries()
    {
        byte[] config = [.. "RIPGREP_CONFIG_PATH=/tmp/rg.conf"u8, 0x00];
        byte[] invalid = [.. "SCOUT_INVALID="u8, 0xFF, 0x00];

        fixed (byte* configPointer = config)
        fixed (byte* invalidPointer = invalid)
        {
            byte** envp = stackalloc byte*[3];
            envp[0] = configPointer;
            envp[1] = invalidPointer;
            envp[2] = null;

            byte[][] environment = ProcessEnvironment.CaptureUnix(envp);

            Assert.Equal(2, environment.Length);
            Assert.Equal("RIPGREP_CONFIG_PATH=/tmp/rg.conf"u8.ToArray(), environment[0]);
            Assert.Equal([.. "SCOUT_INVALID="u8, 0xFF], environment[1]);
            Assert.Equal("/tmp/rg.conf", ProcessEnvironment.GetVariable(environment, "RIPGREP_CONFIG_PATH"));
            Assert.Null(ProcessEnvironment.GetVariable(environment, "SCOUT_INVALID"));
            Assert.Null(ProcessEnvironment.GetVariable(environment, "MISSING"));
        }
    }
}
