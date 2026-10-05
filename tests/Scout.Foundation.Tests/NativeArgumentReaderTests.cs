
namespace Scout;

/// <summary>
/// Verifies native argv capture behavior.
/// </summary>
[TestClass]
public sealed unsafe class NativeArgumentReaderTests
{
    /// <summary>
    /// Verifies Unix argv capture preserves non-UTF-8 argument bytes.
    /// </summary>
    [TestMethod]
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

            Assert.HasCount(3, arguments);
            Assert.AreSequenceEqual<byte>([0x73, 0x63, 0x6f, 0x75, 0x74], arguments[0].AsUnixBytes().ToArray());
            Assert.AreSequenceEqual<byte>([0x2d, 0x56], arguments[1].AsUnixBytes().ToArray());
            Assert.AreSequenceEqual<byte>([0xff, 0x80], arguments[2].AsUnixBytes().ToArray());
        }
    }

    /// <summary>
    /// Verifies Windows argv capture preserves UTF-16 argument text.
    /// </summary>
    [TestMethod]
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

            Assert.HasCount(3, arguments);
            Assert.AreEqual("scout", arguments[0].AsWindowsString());
            Assert.AreEqual("-V", arguments[1].AsWindowsString());
            Assert.AreEqual("C:\\tmp\\file.txt", arguments[2].AsWindowsString());
        }
    }

    /// <summary>
    /// Verifies Unix environment capture preserves raw entries and resolves UTF-8 values without lossy fallback.
    /// </summary>
    [TestMethod]
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

            Assert.HasCount(2, environment);
            Assert.AreSequenceEqual("RIPGREP_CONFIG_PATH=/tmp/rg.conf"u8.ToArray(), environment[0]);
            Assert.AreSequenceEqual<byte>([.. "SCOUT_INVALID="u8, 0xFF], environment[1]);
            Assert.AreEqual("/tmp/rg.conf", ProcessEnvironment.GetVariable(environment, "RIPGREP_CONFIG_PATH"));
            Assert.AreSequenceEqual("/tmp/rg.conf"u8.ToArray(), ProcessEnvironment.GetVariableOsString(environment, "RIPGREP_CONFIG_PATH")!.Value.AsUnixBytes().ToArray());
            Assert.IsNull(ProcessEnvironment.GetVariable(environment, "SCOUT_INVALID"));
            Assert.AreSequenceEqual<byte>([0xFF], ProcessEnvironment.GetVariableOsString(environment, "SCOUT_INVALID")!.Value.AsUnixBytes().ToArray());
            Assert.IsNull(ProcessEnvironment.GetVariable(environment, "MISSING"));
            Assert.IsNull(ProcessEnvironment.GetVariableOsString(environment, "MISSING"));
        }
    }
}
