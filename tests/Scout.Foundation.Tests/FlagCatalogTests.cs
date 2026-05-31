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
        Assert.True(GeneratedFlagCatalog.TryFindLongSwitch("--ignore-vcs", out FlagDescriptor ignoreVcs));
        Assert.Equal("--ignore-vcs", ignoreVcs.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongSwitch("--passthrough", out FlagDescriptor passthrough));
        Assert.Equal("--passthru", passthrough.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortSwitch('u', out FlagDescriptor unrestricted));
        Assert.Equal("--unrestricted", unrestricted.LongName);
    }

    /// <summary>
    /// Verifies the generated catalog contains migrated required-value definitions.
    /// </summary>
    [Fact]
    public void CatalogContainsMigratedValueDefinitions()
    {
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--max-count", out FlagDescriptor maxCount));
        Assert.Equal("--max-count", maxCount.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('M', out FlagDescriptor maxColumns));
        Assert.Equal("--max-columns", maxColumns.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('E', out FlagDescriptor encoding));
        Assert.Equal("--encoding", encoding.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--engine", out FlagDescriptor engine));
        Assert.Equal("--engine", engine.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--color", out FlagDescriptor color));
        Assert.Equal("--color", color.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--generate", out FlagDescriptor generate));
        Assert.Equal("--generate", generate.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--colors", out FlagDescriptor colors));
        Assert.Equal("--colors", colors.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--dfa-size-limit", out FlagDescriptor dfaSizeLimit));
        Assert.Equal("--dfa-size-limit", dfaSizeLimit.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--regex-size-limit", out FlagDescriptor regexSizeLimit));
        Assert.Equal("--regex-size-limit", regexSizeLimit.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('j', out FlagDescriptor threads));
        Assert.Equal("--threads", threads.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('A', out FlagDescriptor afterContext));
        Assert.Equal("--after-context", afterContext.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('B', out FlagDescriptor beforeContext));
        Assert.Equal("--before-context", beforeContext.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('C', out FlagDescriptor context));
        Assert.Equal("--context", context.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--maxdepth", out FlagDescriptor maxDepth));
        Assert.Equal("--max-depth", maxDepth.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--regexp", out FlagDescriptor regexp));
        Assert.Equal("--regexp", regexp.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('e', out FlagDescriptor shortRegexp));
        Assert.Equal("--regexp", shortRegexp.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--replace", out FlagDescriptor replace));
        Assert.Equal("--replace", replace.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('r', out FlagDescriptor shortReplace));
        Assert.Equal("--replace", shortReplace.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--file", out FlagDescriptor file));
        Assert.Equal("--file", file.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('f', out FlagDescriptor shortFile));
        Assert.Equal("--file", shortFile.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--glob", out FlagDescriptor glob));
        Assert.Equal("--glob", glob.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('g', out FlagDescriptor shortGlob));
        Assert.Equal("--glob", shortGlob.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--iglob", out FlagDescriptor iglob));
        Assert.Equal("--iglob", iglob.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--type", out FlagDescriptor type));
        Assert.Equal("--type", type.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('t', out FlagDescriptor shortType));
        Assert.Equal("--type", shortType.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--type-not", out FlagDescriptor typeNot));
        Assert.Equal("--type-not", typeNot.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortValue('T', out FlagDescriptor shortTypeNot));
        Assert.Equal("--type-not", shortTypeNot.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--type-add", out FlagDescriptor typeAdd));
        Assert.Equal("--type-add", typeAdd.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--type-clear", out FlagDescriptor typeClear));
        Assert.Equal("--type-clear", typeClear.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--max-filesize", out FlagDescriptor maxFileSize));
        Assert.Equal("--max-filesize", maxFileSize.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--sort", out FlagDescriptor sort));
        Assert.Equal("--sort", sort.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--sortr", out FlagDescriptor sortReverse));
        Assert.Equal("--sortr", sortReverse.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--ignore-file", out FlagDescriptor ignoreFile));
        Assert.Equal("--ignore-file", ignoreFile.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--pre", out FlagDescriptor pre));
        Assert.Equal("--pre", pre.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--pre-glob", out FlagDescriptor preGlob));
        Assert.Equal("--pre-glob", preGlob.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--hostname-bin", out FlagDescriptor hostnameBin));
        Assert.Equal("--hostname-bin", hostnameBin.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--hyperlink-format", out FlagDescriptor hyperlinkFormat));
        Assert.Equal("--hyperlink-format", hyperlinkFormat.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--field-match-separator", out FlagDescriptor fieldMatchSeparator));
        Assert.Equal("--field-match-separator", fieldMatchSeparator.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--field-context-separator", out FlagDescriptor fieldContextSeparator));
        Assert.Equal("--field-context-separator", fieldContextSeparator.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--context-separator", out FlagDescriptor contextSeparator));
        Assert.Equal("--context-separator", contextSeparator.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindLongValue("--path-separator", out FlagDescriptor pathSeparator));
        Assert.Equal("--path-separator", pathSeparator.LongName);
    }

    /// <summary>
    /// Verifies the generated catalog contains migrated special-mode definitions.
    /// </summary>
    [Fact]
    public void CatalogContainsMigratedSpecialDefinitions()
    {
        Assert.True(GeneratedFlagCatalog.TryFindLongSpecial("--help", out FlagDescriptor help));
        Assert.Equal("--help", help.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortSpecial('h', out FlagDescriptor shortHelp));
        Assert.Equal("--help", shortHelp.LongName);
        Assert.True(help.TryGetSpecialMode("--help", out CliSpecialMode helpLong));
        Assert.Equal(CliSpecialMode.HelpLong, helpLong);
        Assert.True(shortHelp.TryGetSpecialMode("-h", out CliSpecialMode helpShort));
        Assert.Equal(CliSpecialMode.HelpShort, helpShort);

        Assert.True(GeneratedFlagCatalog.TryFindLongSpecial("--version", out FlagDescriptor version));
        Assert.Equal("--version", version.LongName);
        Assert.True(GeneratedFlagCatalog.TryFindShortSpecial('V', out FlagDescriptor shortVersion));
        Assert.Equal("--version", shortVersion.LongName);
        Assert.True(version.TryGetSpecialMode("--version", out CliSpecialMode versionLong));
        Assert.Equal(CliSpecialMode.VersionLong, versionLong);
        Assert.True(shortVersion.TryGetSpecialMode("-V", out CliSpecialMode versionShort));
        Assert.Equal(CliSpecialMode.VersionShort, versionShort);

        Assert.True(GeneratedFlagCatalog.TryFindLongSpecial("--pcre2-version", out FlagDescriptor pcre2Version));
        Assert.Equal("--pcre2-version", pcre2Version.LongName);
        Assert.True(pcre2Version.TryGetSpecialMode("--pcre2-version", out CliSpecialMode pcre2Mode));
        Assert.Equal(CliSpecialMode.Pcre2Version, pcre2Mode);
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
        CliParseResult ignoreToggles = CliParser.Parse(
            [
                OsString.FromUnixBytes("--no-ignore"u8),
                OsString.FromUnixBytes("--ignore-vcs"u8),
                OsString.FromUnixBytes("--no-ignore-exclude"u8),
                OsString.FromUnixBytes("--ignore-messages"u8),
                OsString.FromUnixBytes("--no-ignore-messages"u8),
            ]);
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
        Assert.Equal(CliParseStatus.Ok, ignoreToggles.Status);
        Assert.False(ignoreToggles.LowArgs!.RespectIgnoreFiles);
        Assert.True(ignoreToggles.LowArgs.RespectGitIgnoreFiles);
        Assert.False(ignoreToggles.LowArgs.RespectGitExcludeFiles);
        Assert.False(ignoreToggles.LowArgs.IgnoreMessages);
        Assert.Equal(CliParseStatus.Ok, ioToggles.Status);
        Assert.Equal(CliBufferMode.Auto, ioToggles.LowArgs!.BufferMode);
        Assert.Equal(CliMmapMode.Never, ioToggles.LowArgs.MmapMode);
        Assert.True(ioToggles.LowArgs.Crlf);
        Assert.True(ioToggles.LowArgs.NullData);
    }

    /// <summary>
    /// Verifies parser value behavior is routed through generated flag definitions.
    /// </summary>
    [Fact]
    public void ParserUsesGeneratedValueDefinitions()
    {
        CliParseResult numericValues = CliParser.Parse(
            [
                OsString.FromUnixBytes("--max-count=10"u8),
                OsString.FromUnixBytes("-M=16"u8),
                OsString.FromUnixBytes("-j4"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult encodingValue = CliParser.Parse(
            [
                OsString.FromUnixBytes("-Eutf-16"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult regexEngine = CliParser.Parse(
            [
                OsString.FromUnixBytes("--engine=auto"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult colorValue = CliParser.Parse(
            [
                OsString.FromUnixBytes("--color=ansi"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult generateValue = CliParser.Parse(
            [
                OsString.FromUnixBytes("--generate=complete-fish"u8),
            ]);
        CliParseResult colorsValue = CliParser.Parse(
            [
                OsString.FromUnixBytes("--colors=path:none"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult sizeLimits = CliParser.Parse(
            [
                OsString.FromUnixBytes("--dfa-size-limit=9G"u8),
                OsString.FromUnixBytes("--regex-size-limit"u8),
                OsString.FromUnixBytes("2M"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);
        CliParseResult contextValues = CliParser.Parse(
            [
                OsString.FromUnixBytes("-A=2"u8),
                OsString.FromUnixBytes("-B3"u8),
                OsString.FromUnixBytes("--context=4"u8),
                OsString.FromUnixBytes("--maxdepth=5"u8),
                OsString.FromUnixBytes("needle"u8),
            ]);

        Assert.Equal(CliParseStatus.Ok, numericValues.Status);
        Assert.Equal(10UL, numericValues.LowArgs!.MaxCount);
        Assert.Equal(16UL, numericValues.LowArgs.MaxColumns);
        Assert.Equal(4UL, numericValues.LowArgs.Threads);
        Assert.Single(numericValues.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, encodingValue.Status);
        Assert.Equal(CliEncodingMode.Utf16, encodingValue.LowArgs!.EncodingMode);
        Assert.Single(encodingValue.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, regexEngine.Status);
        Assert.Equal(CliRegexEngine.Auto, regexEngine.LowArgs!.RegexEngine);
        Assert.Single(regexEngine.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, colorValue.Status);
        Assert.Equal(CliColorMode.Ansi, colorValue.LowArgs!.ColorMode);
        Assert.Single(colorValue.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, generateValue.Status);
        Assert.Equal(CliGenerateMode.CompleteFish, generateValue.LowArgs!.GenerateMode);
        Assert.Empty(generateValue.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, colorsValue.Status);
        Assert.Equal(["path:none"], colorsValue.LowArgs!.ColorSpecs);
        Assert.Single(colorsValue.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, sizeLimits.Status);
        Assert.Equal(9UL * 1024UL * 1024UL * 1024UL, sizeLimits.LowArgs!.DfaSizeLimit);
        Assert.Equal(2UL * 1024UL * 1024UL, sizeLimits.LowArgs.RegexSizeLimit);
        Assert.Single(sizeLimits.LowArgs.Positional);
        Assert.Equal(CliParseStatus.Ok, contextValues.Status);
        Assert.Equal(2UL, contextValues.LowArgs!.AfterContext);
        Assert.Equal(3UL, contextValues.LowArgs.BeforeContext);
        Assert.Equal(5UL, contextValues.LowArgs.MaxDepth);
        Assert.Single(contextValues.LowArgs.Positional);

        CliParseResult remainingValues = CliParser.Parse(
            [
                OsString.FromUnixBytes("-efoo"u8),
                OsString.FromUnixBytes("-rbar"u8),
                OsString.FromUnixBytes("-g*.cs"u8),
                OsString.FromUnixBytes("--iglob=*.md"u8),
                OsString.FromUnixBytes("--sort=path"u8),
                OsString.FromUnixBytes("--sortr"u8),
                OsString.FromUnixBytes("modified"u8),
                OsString.FromUnixBytes("-trust"u8),
                OsString.FromUnixBytes("-Tgo"u8),
                OsString.FromUnixBytes("--type-add"u8),
                OsString.FromUnixBytes("proto:*.proto"u8),
                OsString.FromUnixBytes("--type-clear=py"u8),
                OsString.FromUnixBytes("--max-filesize=1M"u8),
                OsString.FromUnixBytes("--field-match-separator=:"u8),
                OsString.FromUnixBytes("--field-context-separator=|"u8),
                OsString.FromUnixBytes("--context-separator=---"u8),
                OsString.FromUnixBytes("--path-separator=/"u8),
                OsString.FromUnixBytes("--ignore-file=.ignore-extra"u8),
                OsString.FromUnixBytes("--pre=cat"u8),
                OsString.FromUnixBytes("--pre-glob=*.txt"u8),
                OsString.FromUnixBytes("--hostname-bin=hostname"u8),
                OsString.FromUnixBytes("--hyperlink-format=none"u8),
            ]);

        Assert.Equal(CliParseStatus.Ok, remainingValues.Status);
        Assert.Single(remainingValues.LowArgs!.Patterns);
        Assert.Equal("bar"u8.ToArray(), remainingValues.LowArgs.Replacement!.Value.ToArray());
        Assert.Equal(2, remainingValues.LowArgs.GlobPatterns.Count);
        Assert.NotNull(remainingValues.LowArgs.SortMode);
        Assert.Equal(CliSortKind.LastModified, remainingValues.LowArgs.SortMode.Value.Kind);
        Assert.True(remainingValues.LowArgs.SortMode.Value.Reverse);
        Assert.Contains(remainingValues.LowArgs.TypeChanges, static change => change.Kind == CliTypeChangeKind.Select && change.Value == "rust");
        Assert.Contains(remainingValues.LowArgs.TypeChanges, static change => change.Kind == CliTypeChangeKind.Negate && change.Value == "go");
        Assert.Contains(remainingValues.LowArgs.TypeChanges, static change => change.Kind == CliTypeChangeKind.Add && change.Value == "proto:*.proto");
        Assert.Contains(remainingValues.LowArgs.TypeChanges, static change => change.Kind == CliTypeChangeKind.Clear && change.Value == "py");
        Assert.Equal(1024UL * 1024UL, remainingValues.LowArgs.MaxFileSize);
        Assert.Equal(":"u8.ToArray(), remainingValues.LowArgs.FieldMatchSeparator.ToArray());
        Assert.Equal("|"u8.ToArray(), remainingValues.LowArgs.FieldContextSeparator.ToArray());
        Assert.Equal("---"u8.ToArray(), remainingValues.LowArgs.ContextSeparator.ToArray());
        Assert.Equal((byte)'/', remainingValues.LowArgs.PathSeparator);
        Assert.Equal([".ignore-extra"], remainingValues.LowArgs.IgnoreFiles);
        Assert.Equal("cat", remainingValues.LowArgs.Preprocessor);
        Assert.Equal(["*.txt"], remainingValues.LowArgs.PreprocessorGlobs);
        Assert.Equal("hostname", remainingValues.LowArgs.HostnameBin);
        Assert.Equal(string.Empty, remainingValues.LowArgs.HyperlinkFormat);
    }

    /// <summary>
    /// Verifies parser special-mode behavior is routed through generated flag definitions.
    /// </summary>
    [Fact]
    public void ParserUsesGeneratedSpecialDefinitions()
    {
        CliParseResult helpShort = CliParser.Parse([OsString.FromUnixBytes("-h"u8)]);
        CliParseResult helpLong = CliParser.Parse([OsString.FromUnixBytes("--help"u8)]);
        CliParseResult versionShort = CliParser.Parse([OsString.FromUnixBytes("-V"u8)]);
        CliParseResult versionLong = CliParser.Parse([OsString.FromUnixBytes("--version"u8)]);
        CliParseResult pcre2Version = CliParser.Parse([OsString.FromUnixBytes("--pcre2-version"u8)]);
        CliParseResult clusteredHelp = CliParser.Parse([OsString.FromUnixBytes("-nh"u8)]);

        Assert.Equal(CliParseStatus.Special, helpShort.Status);
        Assert.Equal(CliSpecialMode.HelpShort, helpShort.SpecialMode);
        Assert.Equal(CliParseStatus.Special, helpLong.Status);
        Assert.Equal(CliSpecialMode.HelpLong, helpLong.SpecialMode);
        Assert.Equal(CliParseStatus.Special, versionShort.Status);
        Assert.Equal(CliSpecialMode.VersionShort, versionShort.SpecialMode);
        Assert.Equal(CliParseStatus.Special, versionLong.Status);
        Assert.Equal(CliSpecialMode.VersionLong, versionLong.SpecialMode);
        Assert.Equal(CliParseStatus.Special, pcre2Version.Status);
        Assert.Equal(CliSpecialMode.Pcre2Version, pcre2Version.SpecialMode);
        Assert.Equal(CliParseStatus.Special, clusteredHelp.Status);
        Assert.Equal(CliSpecialMode.HelpShort, clusteredHelp.SpecialMode);
    }

    /// <summary>
    /// Verifies spelling-sensitive generated switch diagnostics match ripgrep wording.
    /// </summary>
    [Fact]
    public void ParserUsesMatchedGeneratedSwitchNameInDiagnostics()
    {
        CliParseResult shortError = CliParser.Parse(
            [OsString.FromUnixBytes("-uuuu"u8)]);
        CliParseResult longError = CliParser.Parse(
            [
                OsString.FromUnixBytes("--unrestricted"u8),
                OsString.FromUnixBytes("--unrestricted"u8),
                OsString.FromUnixBytes("--unrestricted"u8),
                OsString.FromUnixBytes("--unrestricted"u8),
            ]);

        Assert.Equal(CliParseStatus.Error, shortError.Status);
        Assert.Equal("error parsing flag -u: flag can only be repeated up to 3 times", shortError.Error!.FormatAlternate());
        Assert.Equal(CliParseStatus.Error, longError.Status);
        Assert.Equal("error parsing flag --unrestricted: flag can only be repeated up to 3 times", longError.Error!.FormatAlternate());
    }
}
