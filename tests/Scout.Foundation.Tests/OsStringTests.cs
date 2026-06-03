using System.Text;

namespace Scout;

/// <summary>
/// Verifies operating-system string preservation.
/// </summary>
public sealed class OsStringTests
{
    /// <summary>
    /// Verifies Unix byte strings preserve bytes that are invalid UTF-8.
    /// </summary>
    [Fact]
    public void UnixBytesPreserveInvalidUtf8()
    {
        var value = OsString.FromUnixBytes([0x66, 0xff, 0x00, 0x80]);

        Assert.True(value.IsUnixBytes);
        Assert.Equal([0x66, 0xff, 0x00, 0x80], value.AsUnixBytes().ToArray());
    }

    /// <summary>
    /// Verifies Windows text does not expose a false Unix byte view.
    /// </summary>
    [Fact]
    public void WindowsTextRejectsUnixByteAccess()
    {
        var value = OsString.FromWindowsString("abc");

        Assert.True(value.IsWindowsText);
        Assert.Equal("abc", value.AsWindowsString());
        Assert.Throws<InvalidOperationException>(() => value.AsUnixBytes());
    }

    /// <summary>
    /// Verifies semantic text uses the current platform representation.
    /// </summary>
    [Fact]
    public void FromTextUsesPlatformRepresentation()
    {
        var value = OsString.FromText("a\u2603");

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("a\u2603", value.AsWindowsString());
        }
        else
        {
            Assert.Equal(Encoding.UTF8.GetBytes("a\u2603"), value.AsUnixBytes().ToArray());
        }
    }
}
