using System.Runtime.InteropServices;

namespace Scout.IO.Ignore;

internal readonly struct UnixStatLayout
{
    public UnixStatLayout(int modeOffset, int inodeOffset, int inodeSize, int sizeOffset)
    {
        ModeOffset = modeOffset;
        InodeOffset = inodeOffset;
        InodeSize = inodeSize;
        SizeOffset = sizeOffset;
    }

    public int ModeOffset { get; }

    public int InodeOffset { get; }

    public int InodeSize { get; }

    public int SizeOffset { get; }

    public static ReadOnlySpan<UnixStatLayout> ForCurrentPlatform =>
        OperatingSystem.IsMacOS()
            ? MacOSLayouts
            : LinuxLayouts;

    private static ReadOnlySpan<UnixStatLayout> MacOSLayouts =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? MacOSX64Layouts
            : MacOSInode64Layouts;

    private static ReadOnlySpan<UnixStatLayout> LinuxLayouts =>
        LinuxStatLayouts;

    private static readonly UnixStatLayout[] MacOSX64Layouts =
    [
        new UnixStatLayout(8, 4, sizeof(uint), 72),
        new UnixStatLayout(4, 8, sizeof(ulong), 96),
    ];

    private static readonly UnixStatLayout[] MacOSInode64Layouts =
    [
        new UnixStatLayout(4, 8, sizeof(ulong), 96),
        new UnixStatLayout(8, 4, sizeof(uint), 72),
    ];

    private static readonly UnixStatLayout[] LinuxStatLayouts =
    [
        new UnixStatLayout(16, 8, sizeof(ulong), 48),
        new UnixStatLayout(24, 8, sizeof(ulong), 48),
    ];
}
