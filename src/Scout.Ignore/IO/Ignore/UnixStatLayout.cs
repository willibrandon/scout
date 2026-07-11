using System.Runtime.InteropServices;

namespace Scout.IO.Ignore;

/// <summary>
/// Describes the native offsets Scout reads from a Unix <c>stat</c> buffer.
/// </summary>
/// <param name="modeOffset">The byte offset of the file mode.</param>
/// <param name="inodeOffset">The byte offset of the inode.</param>
/// <param name="inodeSize">The inode width in bytes.</param>
/// <param name="sizeOffset">The byte offset of the file length.</param>
internal readonly struct UnixStatLayout(int modeOffset, int inodeOffset, int inodeSize, int sizeOffset)
{
    /// <summary>
    /// Gets the byte offset of the file mode.
    /// </summary>
    public int ModeOffset { get; } = modeOffset;

    /// <summary>
    /// Gets the byte offset of the inode.
    /// </summary>
    public int InodeOffset { get; } = inodeOffset;

    /// <summary>
    /// Gets the inode width in bytes.
    /// </summary>
    public int InodeSize { get; } = inodeSize;

    /// <summary>
    /// Gets the byte offset of the file length.
    /// </summary>
    public int SizeOffset { get; } = sizeOffset;

    /// <summary>
    /// Gets the native layouts for the current platform in preferred order.
    /// </summary>
    public static ReadOnlySpan<UnixStatLayout> ForCurrentPlatform =>
        OperatingSystem.IsMacOS()
            ? MacOSLayouts
            : LinuxLayouts;

    private static ReadOnlySpan<UnixStatLayout> MacOSLayouts =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? _macOsX64Layouts
            : _macOsInode64Layouts;

    private static ReadOnlySpan<UnixStatLayout> LinuxLayouts =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => _linuxX64Layouts,
            Architecture.Arm64 => _linuxArm64Layouts,
            _ => _linuxFallbackLayouts,
        };

    private static readonly UnixStatLayout[] _macOsX64Layouts =
    [
        new UnixStatLayout(8, 4, sizeof(uint), 72),
        new UnixStatLayout(4, 8, sizeof(ulong), 96),
    ];

    private static readonly UnixStatLayout[] _macOsInode64Layouts =
    [
        new UnixStatLayout(4, 8, sizeof(ulong), 96),
        new UnixStatLayout(8, 4, sizeof(uint), 72),
    ];

    private static readonly UnixStatLayout[] _linuxX64Layouts =
    [
        new UnixStatLayout(24, 8, sizeof(ulong), 48),
        new UnixStatLayout(16, 8, sizeof(ulong), 48),
    ];

    private static readonly UnixStatLayout[] _linuxArm64Layouts =
    [
        new UnixStatLayout(16, 8, sizeof(ulong), 48),
        new UnixStatLayout(24, 8, sizeof(ulong), 48),
    ];

    private static readonly UnixStatLayout[] _linuxFallbackLayouts =
    [
        new UnixStatLayout(16, 8, sizeof(ulong), 48),
        new UnixStatLayout(24, 8, sizeof(ulong), 48),
    ];
}
