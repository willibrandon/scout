using System.Text;

namespace Scout;

/// <summary>
/// Verifies operating-system string preservation.
/// </summary>
[TestClass]
public sealed class OsStringTests
{
    /// <summary>
    /// Verifies Unix byte strings preserve bytes that are invalid UTF-8.
    /// </summary>
    [TestMethod]
    public void UnixBytesPreserveInvalidUtf8()
    {
        var value = OsString.FromUnixBytes([0x66, 0xff, 0x00, 0x80]);

        Assert.IsTrue(value.IsUnixBytes);
        Assert.AreSequenceEqual<byte>([0x66, 0xff, 0x00, 0x80], value.AsUnixBytes().ToArray());
    }

    /// <summary>
    /// Verifies Windows text does not expose a false Unix byte view.
    /// </summary>
    [TestMethod]
    public void WindowsTextRejectsUnixByteAccess()
    {
        var value = OsString.FromWindowsString("abc");

        Assert.IsTrue(value.IsWindowsText);
        Assert.AreEqual("abc", value.AsWindowsString());
        Assert.ThrowsExactly<InvalidOperationException>(() => value.AsUnixBytes());
    }

    /// <summary>
    /// Verifies semantic text uses the current platform representation.
    /// </summary>
    [TestMethod]
    public void FromTextUsesPlatformRepresentation()
    {
        var value = OsString.FromText("a\u2603");

        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual("a\u2603", value.AsWindowsString());
        }
        else
        {
            Assert.AreSequenceEqual(Encoding.UTF8.GetBytes("a\u2603"), value.AsUnixBytes().ToArray());
        }
    }
}
