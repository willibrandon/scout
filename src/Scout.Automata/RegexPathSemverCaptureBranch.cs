namespace Scout;

internal sealed class RegexPathSemverCaptureBranch
{
    public RegexPathSemverCaptureBranch(
        RegexPathSemverPrefixPart[] prefixParts,
        byte[] searchPrefix,
        bool[] directoryByteMatches,
        bool[] directorySeparatorMatches,
        int nameCaptureIndex,
        bool[] nameByteMatches,
        int versionCaptureIndex,
        bool[] majorByteMatches,
        bool[] minorByteMatches,
        bool[] patchByteMatches,
        bool[] versionTailByteMatches,
        bool[] trailingSeparatorMatches)
    {
        PrefixParts = prefixParts;
        SearchPrefix = searchPrefix;
        DirectoryByteMatches = directoryByteMatches;
        DirectorySeparatorMatches = directorySeparatorMatches;
        NameCaptureIndex = nameCaptureIndex;
        NameByteMatches = nameByteMatches;
        VersionCaptureIndex = versionCaptureIndex;
        MajorByteMatches = majorByteMatches;
        MinorByteMatches = minorByteMatches;
        PatchByteMatches = patchByteMatches;
        VersionTailByteMatches = versionTailByteMatches;
        TrailingSeparatorMatches = trailingSeparatorMatches;
    }

    public RegexPathSemverPrefixPart[] PrefixParts { get; }

    public byte[] SearchPrefix { get; }

    public bool[] DirectoryByteMatches { get; }

    public bool[] DirectorySeparatorMatches { get; }

    public int NameCaptureIndex { get; }

    public bool[] NameByteMatches { get; }

    public int VersionCaptureIndex { get; }

    public bool[] MajorByteMatches { get; }

    public bool[] MinorByteMatches { get; }

    public bool[] PatchByteMatches { get; }

    public bool[] VersionTailByteMatches { get; }

    public bool[] TrailingSeparatorMatches { get; }
}
