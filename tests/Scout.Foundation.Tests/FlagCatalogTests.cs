using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Verifies the generated command-line flag catalog.
/// </summary>
public sealed class FlagCatalogTests
{
    /// <summary>
    /// Verifies the initial generated catalog contains the migrated switch definitions.
    /// </summary>
    [Fact]
    public void CatalogContainsMigratedSwitchDefinitions()
    {
        Assert.True(GeneratedFlagCatalog.TryFindLongSwitch("--files", out FlagDescriptor files));
        Assert.Equal("--files", files.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongSwitch("--no-json", out FlagDescriptor noJson));
        Assert.Equal("--no-json", noJson.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortSwitch('c', out FlagDescriptor count));
        Assert.Equal("--count", count.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortSwitch('U', out FlagDescriptor multiline));
        Assert.Equal("--multiline", multiline.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortSwitch('n', out FlagDescriptor lineNumber));
        Assert.Equal("--line-number", lineNumber.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortSwitch('.', out FlagDescriptor hidden));
        Assert.Equal("--hidden", hidden.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongSwitch("--no-follow", out FlagDescriptor noFollow));
        Assert.Equal("--no-follow", noFollow.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongSwitch("--search-zip", out FlagDescriptor searchZip));
        Assert.Equal("--search-zip", searchZip.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongSwitch("--no-mmap", out FlagDescriptor noMmap));
        Assert.Equal("--no-mmap", noMmap.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongSwitch("--multiline-dotall", out FlagDescriptor multilineDotall));
        Assert.Equal("--multiline-dotall", multilineDotall.LongName);
    }

    /// <summary>
    /// Verifies generated flag descriptors do not define duplicate canonical spellings.
    /// </summary>
    [Fact]
    public void CatalogHasUniqueCanonicalSpellings()
    {
        var longNames = new HashSet<string>();
        var shortNames = new HashSet<char>();
        ReadOnlySpan<FlagDescriptor> descriptors = GeneratedFlagCatalog.Descriptors;

        for (int index = 0; index < descriptors.Length; index++)
        {
            FlagDescriptor descriptor = descriptors[index];
            Assert.True(longNames.Add(descriptor.LongName), "Duplicate long flag: " + descriptor.LongName);
            if (descriptor.ShortName is char shortName)
            {
                Assert.True(shortNames.Add(shortName), "Duplicate short flag: " + shortName);
            }
        }
    }

    /// <summary>
    /// Verifies parser switch behavior is routed through generated flag definitions.
    /// </summary>
    [Fact]
    public void ParserUsesGeneratedSwitchDefinitions()
    {
        CliParseResult files = CliParser.Parse([OsString.FromUnixBytes("--files"u8)]);
        CliParseResult countCluster = CliParser.Parse([OsString.FromUnixBytes("-cU"u8)]);
        CliParseResult outputCluster = CliParser.Parse([OsString.FromUnixBytes("-nHiv"u8), OsString.FromUnixBytes("needle"u8)]);
        CliParseResult jsonReset = CliParser.Parse([OsString.FromUnixBytes("--json"u8), OsString.FromUnixBytes("--no-json"u8)]);
        CliParseResult ioToggles = CliParser.Parse(
            [
                OsString.FromUnixBytes("--line-buffered"u8),
                OsString.FromUnixBytes("--no-line-buffered"u8),
                OsString.FromUnixBytes("--mmap"u8),
                OsString.FromUnixBytes("--no-mmap"u8),
                OsString.FromUnixBytes("--crlf"u8),
                OsString.FromUnixBytes("--null-data"u8),
            ]);

        Assert.Equal(CliParseStatus.Ok, files.Status);
        Assert.Equal(CliSearchMode.Files, files.LowArgs!.SearchMode);
        Assert.Equal(CliParseStatus.Ok, countCluster.Status);
        Assert.Equal(CliSearchMode.Count, countCluster.LowArgs!.SearchMode);
        Assert.True(countCluster.LowArgs.Multiline);
        Assert.Equal(CliParseStatus.Ok, outputCluster.Status);
        Assert.True(outputCluster.LowArgs!.LineNumber);
        Assert.True(outputCluster.LowArgs.WithFilename);
        Assert.Equal(CliCaseMode.Insensitive, outputCluster.LowArgs.CaseMode);
        Assert.True(outputCluster.LowArgs.InvertMatch);
        Assert.Equal(CliParseStatus.Ok, jsonReset.Status);
        Assert.Equal(CliSearchMode.Standard, jsonReset.LowArgs!.SearchMode);
        Assert.Equal(CliParseStatus.Ok, ioToggles.Status);
        Assert.Equal(CliBufferMode.Auto, ioToggles.LowArgs!.BufferMode);
        Assert.Equal(CliMmapMode.Never, ioToggles.LowArgs.MmapMode);
        Assert.True(ioToggles.LowArgs.Crlf);
        Assert.True(ioToggles.LowArgs.NullData);
    }
}
