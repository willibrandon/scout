using System.Text;

namespace Scout;

/// <summary>
/// Verifies the generated command-line flag catalog.
/// </summary>
[TestClass]
public sealed class FlagCatalogTests
{
    /// <summary>
    /// Verifies the initial generated catalog contains the migrated switch definitions.
    /// </summary>
    [TestMethod]
    public void CatalogContainsMigratedSwitchDefinitions()
    {
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--files", out FlagDescriptor files));
        Assert.AreEqual("--files", files.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-json", out FlagDescriptor noJson));
        Assert.AreEqual("--json", noJson.LongName);
        Assert.AreEqual("--no-json", noJson.NegatedName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSwitch('c', out FlagDescriptor count));
        Assert.AreEqual("--count", count.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSwitch('U', out FlagDescriptor multiline));
        Assert.AreEqual("--multiline", multiline.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSwitch('n', out FlagDescriptor lineNumber));
        Assert.AreEqual("--line-number", lineNumber.LongName);
        Assert.IsNull(lineNumber.NegatedName);
        Assert.IsNull(lineNumber.NegatedShortName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSwitch('N', out FlagDescriptor noLineNumber));
        Assert.AreEqual("--no-line-number", noLineNumber.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSwitch('.', out FlagDescriptor hidden));
        Assert.AreEqual("--hidden", hidden.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSwitch('H', out FlagDescriptor withFilename));
        Assert.AreEqual("--with-filename", withFilename.LongName);
        Assert.IsNull(withFilename.NegatedName);
        Assert.IsNull(withFilename.NegatedShortName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSwitch('I', out FlagDescriptor noFilename));
        Assert.AreEqual("--no-filename", noFilename.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-follow", out FlagDescriptor noFollow));
        Assert.AreEqual("--follow", noFollow.LongName);
        Assert.AreEqual("--no-follow", noFollow.NegatedName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--search-zip", out FlagDescriptor searchZip));
        Assert.AreEqual("--search-zip", searchZip.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-search-zip", out FlagDescriptor noSearchZip));
        Assert.AreEqual("--search-zip", noSearchZip.LongName);
        Assert.AreEqual("--no-search-zip", noSearchZip.NegatedName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-mmap", out FlagDescriptor noMmap));
        Assert.AreEqual("--mmap", noMmap.LongName);
        Assert.AreEqual("--no-mmap", noMmap.NegatedName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--multiline-dotall", out FlagDescriptor multilineDotall));
        Assert.AreEqual("--multiline-dotall", multilineDotall.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--ignore-vcs", out FlagDescriptor ignoreVcs));
        Assert.AreEqual("--ignore-vcs", ignoreVcs.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--passthrough", out FlagDescriptor passthrough));
        Assert.AreEqual("--passthru", passthrough.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSwitch('u', out FlagDescriptor unrestricted));
        Assert.AreEqual("--unrestricted", unrestricted.LongName);
    }

    /// <summary>
    /// Verifies the generated catalog contains migrated required-value definitions.
    /// </summary>
    [TestMethod]
    public void CatalogContainsMigratedValueDefinitions()
    {
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--max-count", out FlagDescriptor maxCount));
        Assert.AreEqual("--max-count", maxCount.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('M', out FlagDescriptor maxColumns));
        Assert.AreEqual("--max-columns", maxColumns.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('E', out FlagDescriptor encoding));
        Assert.AreEqual("--encoding", encoding.LongName);
        Assert.AreEqual("--no-encoding", encoding.NegatedName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-encoding", out FlagDescriptor noEncoding));
        Assert.AreEqual("--encoding", noEncoding.LongName);
        Assert.AreEqual(FlagKind.Value, noEncoding.Kind);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--engine", out FlagDescriptor engine));
        Assert.AreEqual("--engine", engine.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--color", out FlagDescriptor color));
        Assert.AreEqual("--color", color.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--generate", out FlagDescriptor generate));
        Assert.AreEqual("--generate", generate.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--colors", out FlagDescriptor colors));
        Assert.AreEqual("--colors", colors.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--dfa-size-limit", out FlagDescriptor dfaSizeLimit));
        Assert.AreEqual("--dfa-size-limit", dfaSizeLimit.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--regex-size-limit", out FlagDescriptor regexSizeLimit));
        Assert.AreEqual("--regex-size-limit", regexSizeLimit.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('j', out FlagDescriptor threads));
        Assert.AreEqual("--threads", threads.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('A', out FlagDescriptor afterContext));
        Assert.AreEqual("--after-context", afterContext.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('B', out FlagDescriptor beforeContext));
        Assert.AreEqual("--before-context", beforeContext.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('C', out FlagDescriptor context));
        Assert.AreEqual("--context", context.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--maxdepth", out FlagDescriptor maxDepth));
        Assert.AreEqual("--max-depth", maxDepth.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--regexp", out FlagDescriptor regexp));
        Assert.AreEqual("--regexp", regexp.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('e', out FlagDescriptor shortRegexp));
        Assert.AreEqual("--regexp", shortRegexp.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--replace", out FlagDescriptor replace));
        Assert.AreEqual("--replace", replace.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('r', out FlagDescriptor shortReplace));
        Assert.AreEqual("--replace", shortReplace.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--file", out FlagDescriptor file));
        Assert.AreEqual("--file", file.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('f', out FlagDescriptor shortFile));
        Assert.AreEqual("--file", shortFile.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--glob", out FlagDescriptor glob));
        Assert.AreEqual("--glob", glob.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('g', out FlagDescriptor shortGlob));
        Assert.AreEqual("--glob", shortGlob.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--iglob", out FlagDescriptor iglob));
        Assert.AreEqual("--iglob", iglob.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--type", out FlagDescriptor type));
        Assert.AreEqual("--type", type.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('t', out FlagDescriptor shortType));
        Assert.AreEqual("--type", shortType.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--type-not", out FlagDescriptor typeNot));
        Assert.AreEqual("--type-not", typeNot.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortValue('T', out FlagDescriptor shortTypeNot));
        Assert.AreEqual("--type-not", shortTypeNot.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--type-add", out FlagDescriptor typeAdd));
        Assert.AreEqual("--type-add", typeAdd.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--type-clear", out FlagDescriptor typeClear));
        Assert.AreEqual("--type-clear", typeClear.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--max-filesize", out FlagDescriptor maxFileSize));
        Assert.AreEqual("--max-filesize", maxFileSize.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--sort", out FlagDescriptor sort));
        Assert.AreEqual("--sort", sort.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--sortr", out FlagDescriptor sortReverse));
        Assert.AreEqual("--sortr", sortReverse.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--ignore-file", out FlagDescriptor ignoreFile));
        Assert.AreEqual("--ignore-file", ignoreFile.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--pre", out FlagDescriptor pre));
        Assert.AreEqual("--pre", pre.LongName);
        Assert.AreEqual("--no-pre", pre.NegatedName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-pre", out FlagDescriptor noPre));
        Assert.AreEqual("--pre", noPre.LongName);
        Assert.AreEqual(FlagKind.Value, noPre.Kind);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--pre-glob", out FlagDescriptor preGlob));
        Assert.AreEqual("--pre-glob", preGlob.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--hostname-bin", out FlagDescriptor hostnameBin));
        Assert.AreEqual("--hostname-bin", hostnameBin.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--hyperlink-format", out FlagDescriptor hyperlinkFormat));
        Assert.AreEqual("--hyperlink-format", hyperlinkFormat.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--field-match-separator", out FlagDescriptor fieldMatchSeparator));
        Assert.AreEqual("--field-match-separator", fieldMatchSeparator.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--field-context-separator", out FlagDescriptor fieldContextSeparator));
        Assert.AreEqual("--field-context-separator", fieldContextSeparator.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--context-separator", out FlagDescriptor contextSeparator));
        Assert.AreEqual("--context-separator", contextSeparator.LongName);
        Assert.AreEqual("--no-context-separator", contextSeparator.NegatedName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-context-separator", out FlagDescriptor noContextSeparator));
        Assert.AreEqual("--context-separator", noContextSeparator.LongName);
        Assert.AreEqual(FlagKind.Value, noContextSeparator.Kind);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongValue("--path-separator", out FlagDescriptor pathSeparator));
        Assert.AreEqual("--path-separator", pathSeparator.LongName);
    }

    /// <summary>
    /// Verifies the generated catalog contains migrated special-mode definitions.
    /// </summary>
    [TestMethod]
    public void CatalogContainsMigratedSpecialDefinitions()
    {
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSpecial("--help", out FlagDescriptor help));
        Assert.AreEqual("--help", help.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSpecial('h', out FlagDescriptor shortHelp));
        Assert.AreEqual("--help", shortHelp.LongName);
        Assert.IsTrue(help.TryGetSpecialMode("--help", out CliSpecialMode helpLong));
        Assert.AreEqual(CliSpecialMode.HelpLong, helpLong);
        Assert.IsTrue(shortHelp.TryGetSpecialMode("-h", out CliSpecialMode helpShort));
        Assert.AreEqual(CliSpecialMode.HelpShort, helpShort);

        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSpecial("--version", out FlagDescriptor version));
        Assert.AreEqual("--version", version.LongName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindShortSpecial('V', out FlagDescriptor shortVersion));
        Assert.AreEqual("--version", shortVersion.LongName);
        Assert.IsTrue(version.TryGetSpecialMode("--version", out CliSpecialMode versionLong));
        Assert.AreEqual(CliSpecialMode.VersionLong, versionLong);
        Assert.IsTrue(shortVersion.TryGetSpecialMode("-V", out CliSpecialMode versionShort));
        Assert.AreEqual(CliSpecialMode.VersionShort, versionShort);

        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSpecial("--pcre2-version", out FlagDescriptor pcre2Version));
        Assert.AreEqual("--pcre2-version", pcre2Version.LongName);
        Assert.IsTrue(pcre2Version.TryGetSpecialMode("--pcre2-version", out CliSpecialMode pcre2Mode));
        Assert.AreEqual(CliSpecialMode.Pcre2Version, pcre2Mode);
    }

    /// <summary>
    /// Verifies the generated catalog keeps the pinned upstream logical flag count.
    /// </summary>
    [TestMethod]
    public void CatalogMatchesPinnedUpstreamFlagCount()
    {
        Assert.HasCount(104, GeneratedFlagCatalog.Descriptors);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-line-number", out FlagDescriptor lineNumberNo));
        Assert.AreEqual('N', lineNumberNo.ShortName);
        Assert.IsTrue(GeneratedFlagCatalog.TryFindLongSwitch("--no-filename", out FlagDescriptor withFilenameNo));
        Assert.AreEqual('I', withFilenameNo.ShortName);
    }

    /// <summary>
    /// Verifies the generated catalog preserves the pinned upstream <c>FLAGS</c> ordering.
    /// </summary>
    [TestMethod]
    public void CatalogMatchesPinnedUpstreamFlagOrder()
    {
        string[] expected =
        [
            "--regexp",
            "--file",
            "--after-context",
            "--before-context",
            "--binary",
            "--block-buffered",
            "--byte-offset",
            "--case-sensitive",
            "--color",
            "--colors",
            "--column",
            "--context",
            "--context-separator",
            "--count",
            "--count-matches",
            "--crlf",
            "--debug",
            "--dfa-size-limit",
            "--encoding",
            "--engine",
            "--field-context-separator",
            "--field-match-separator",
            "--files",
            "--files-with-matches",
            "--files-without-match",
            "--fixed-strings",
            "--follow",
            "--generate",
            "--glob",
            "--glob-case-insensitive",
            "--heading",
            "--help",
            "--hidden",
            "--hostname-bin",
            "--hyperlink-format",
            "--iglob",
            "--ignore-case",
            "--ignore-file",
            "--ignore-file-case-insensitive",
            "--include-zero",
            "--invert-match",
            "--json",
            "--line-buffered",
            "--line-number",
            "--no-line-number",
            "--line-regexp",
            "--max-columns",
            "--max-columns-preview",
            "--max-count",
            "--max-depth",
            "--max-filesize",
            "--mmap",
            "--multiline",
            "--multiline-dotall",
            "--no-config",
            "--ignore",
            "--ignore-dot",
            "--ignore-exclude",
            "--ignore-files",
            "--ignore-global",
            "--ignore-messages",
            "--ignore-parent",
            "--ignore-vcs",
            "--messages",
            "--require-git",
            "--unicode",
            "--null",
            "--null-data",
            "--one-file-system",
            "--only-matching",
            "--path-separator",
            "--passthru",
            "--pcre2",
            "--pcre2-version",
            "--pre",
            "--pre-glob",
            "--pretty",
            "--quiet",
            "--regex-size-limit",
            "--replace",
            "--search-zip",
            "--smart-case",
            "--sort",
            "--sortr",
            "--stats",
            "--stop-on-nonmatch",
            "--text",
            "--threads",
            "--trace",
            "--trim",
            "--type",
            "--type-not",
            "--type-add",
            "--type-clear",
            "--type-list",
            "--unrestricted",
            "--version",
            "--vimgrep",
            "--with-filename",
            "--no-filename",
            "--word-regexp",
            "--auto-hybrid-regex",
            "--pcre2-unicode",
            "--sort-files",
        ];

        ReadOnlySpan<FlagDescriptor> descriptors = GeneratedFlagCatalog.Descriptors;
        Assert.HasCount(expected.Length, descriptors);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index], descriptors[index].LongName);
        }
    }

    /// <summary>
    /// Verifies generated flag descriptors do not define duplicate canonical spellings.
    /// </summary>
    [TestMethod]
    public void CatalogHasUniqueCanonicalSpellings()
    {
        var longNames = new HashSet<string>();
        var shortNames = new HashSet<char>();
        ReadOnlySpan<FlagDescriptor> descriptors = GeneratedFlagCatalog.Descriptors;

        for (int index = 0; index < descriptors.Length; index++)
        {
            FlagDescriptor descriptor = descriptors[index];
            Assert.IsTrue(longNames.Add(descriptor.LongName), "Duplicate long flag: " + descriptor.LongName);
            if (descriptor.NegatedName is string negatedName)
            {
                Assert.IsTrue(longNames.Add(negatedName), "Duplicate long flag: " + negatedName);
            }

            if (descriptor.ShortName is char shortName)
            {
                Assert.IsTrue(shortNames.Add(shortName), "Duplicate short flag: " + shortName);
            }

            if (descriptor.NegatedShortName is char negatedShortName)
            {
                Assert.IsTrue(shortNames.Add(negatedShortName), "Duplicate short flag: " + negatedShortName);
            }
        }
    }

    /// <summary>
    /// Verifies generated help and completion artifacts list every generated flag.
    /// </summary>
    [TestMethod]
    public void GeneratedArtifactsListEveryCatalogFlag()
    {
        string longHelp = Encoding.UTF8.GetString(HelpOutput.Long);
        string manPage = Encoding.UTF8.GetString(GenerateOutput.Get(CliGenerateMode.Man));
        string bashCompletion = Encoding.UTF8.GetString(GenerateOutput.Get(CliGenerateMode.CompleteBash));
        string zshCompletion = Encoding.UTF8.GetString(GenerateOutput.Get(CliGenerateMode.CompleteZsh));
        string fishCompletion = Encoding.UTF8.GetString(GenerateOutput.Get(CliGenerateMode.CompleteFish));
        string powerShellCompletion = Encoding.UTF8.GetString(GenerateOutput.Get(CliGenerateMode.CompletePowerShell));
        ReadOnlySpan<FlagDescriptor> descriptors = GeneratedFlagCatalog.Descriptors;

        for (int index = 0; index < descriptors.Length; index++)
        {
            FlagDescriptor descriptor = descriptors[index];
            Assert.Contains(descriptor.LongName, longHelp, StringComparison.Ordinal);
            AssertManContainsOption(manPage, descriptor.LongName);
            Assert.Contains(descriptor.LongName, bashCompletion, StringComparison.Ordinal);
            Assert.Contains(descriptor.LongName, zshCompletion, StringComparison.Ordinal);
            Assert.Contains("-l " + descriptor.LongName[2..], fishCompletion, StringComparison.Ordinal);
            Assert.Contains("::new('" + descriptor.LongName + "'", powerShellCompletion, StringComparison.Ordinal);

            if (descriptor.NegatedName is string negatedName)
            {
                Assert.Contains(negatedName, longHelp, StringComparison.Ordinal);
                AssertManContainsOption(manPage, negatedName);
                Assert.Contains(negatedName, bashCompletion, StringComparison.Ordinal);
                Assert.Contains(negatedName, zshCompletion, StringComparison.Ordinal);
                Assert.Contains("-l " + negatedName[2..], fishCompletion, StringComparison.Ordinal);
                Assert.Contains("::new('" + negatedName + "'", powerShellCompletion, StringComparison.Ordinal);
            }

            if (descriptor.ShortName is char shortName)
            {
                string shortOption = "-" + shortName;
                AssertManContainsOption(manPage, shortOption);
                Assert.Contains(shortOption, bashCompletion, StringComparison.Ordinal);
                Assert.Contains(shortOption, zshCompletion, StringComparison.Ordinal);
                Assert.Contains("-s " + shortName, fishCompletion, StringComparison.Ordinal);
                Assert.Contains("::new('" + shortOption + "'", powerShellCompletion, StringComparison.Ordinal);
            }

            if (descriptor.NegatedShortName is char negatedShortName)
            {
                string negatedShortOption = "-" + negatedShortName;
                AssertManContainsOption(manPage, negatedShortOption);
                Assert.Contains(negatedShortOption, bashCompletion, StringComparison.Ordinal);
                Assert.Contains(negatedShortOption, zshCompletion, StringComparison.Ordinal);
                Assert.Contains("-s " + negatedShortName, fishCompletion, StringComparison.Ordinal);
                Assert.Contains("::new('" + negatedShortOption + "'", powerShellCompletion, StringComparison.Ordinal);
            }
        }
    }

    private static void AssertManContainsOption(string manPage, string option)
    {
        string fullyEscaped = option.Replace("-", "\\-", StringComparison.Ordinal);
        string leadingEscaped = EscapeLeadingManOptionHyphens(option);
        Assert.IsTrue(
            manPage.Contains(fullyEscaped, StringComparison.Ordinal) ||
            manPage.Contains(leadingEscaped, StringComparison.Ordinal) ||
            manPage.Contains(option, StringComparison.Ordinal),
            "Man page does not contain option " + option);
    }

    private static string EscapeLeadingManOptionHyphens(string option)
    {
        if (option.StartsWith("--", StringComparison.Ordinal))
        {
            return "\\-\\-" + option[2..];
        }

        if (option.StartsWith('-'))
        {
            return "\\-" + option[1..];
        }

        return option;
    }

    /// <summary>
    /// Verifies parser switch behavior is routed through generated flag definitions.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(CliParseStatus.Ok, files.Status);
        Assert.AreEqual(CliSearchMode.Files, files.LowArgs!.SearchMode);
        Assert.AreEqual(CliParseStatus.Ok, countCluster.Status);
        Assert.AreEqual(CliSearchMode.Count, countCluster.LowArgs!.SearchMode);
        Assert.IsTrue(countCluster.LowArgs.Multiline);
        Assert.AreEqual(CliParseStatus.Ok, outputCluster.Status);
        Assert.IsTrue(outputCluster.LowArgs!.LineNumber);
        Assert.IsTrue(outputCluster.LowArgs.WithFilename);
        Assert.AreEqual(CliCaseMode.Insensitive, outputCluster.LowArgs.CaseMode);
        Assert.IsTrue(outputCluster.LowArgs.InvertMatch);
        Assert.AreEqual(CliParseStatus.Ok, jsonReset.Status);
        Assert.AreEqual(CliSearchMode.Standard, jsonReset.LowArgs!.SearchMode);
        Assert.AreEqual(CliParseStatus.Ok, ignoreToggles.Status);
        Assert.IsFalse(ignoreToggles.LowArgs!.RespectIgnoreFiles);
        Assert.IsTrue(ignoreToggles.LowArgs.RespectGitIgnoreFiles);
        Assert.IsFalse(ignoreToggles.LowArgs.RespectGitExcludeFiles);
        Assert.IsFalse(ignoreToggles.LowArgs.IgnoreMessages);
        Assert.AreEqual(CliParseStatus.Ok, ioToggles.Status);
        Assert.AreEqual(CliBufferMode.Auto, ioToggles.LowArgs!.BufferMode);
        Assert.AreEqual(CliMmapMode.Never, ioToggles.LowArgs.MmapMode);
        Assert.IsFalse(ioToggles.LowArgs.Crlf);
        Assert.IsTrue(ioToggles.LowArgs.NullData);
    }

    /// <summary>
    /// Verifies parser value behavior is routed through generated flag definitions.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(CliParseStatus.Ok, numericValues.Status);
        Assert.AreEqual(10UL, numericValues.LowArgs!.MaxCount);
        Assert.AreEqual(16UL, numericValues.LowArgs.MaxColumns);
        Assert.AreEqual(4UL, numericValues.LowArgs.Threads);
        Assert.ContainsSingle(numericValues.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, encodingValue.Status);
        Assert.AreEqual(CliEncodingMode.Utf16, encodingValue.LowArgs!.EncodingMode);
        Assert.ContainsSingle(encodingValue.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, regexEngine.Status);
        Assert.AreEqual(CliRegexEngine.Auto, regexEngine.LowArgs!.RegexEngine);
        Assert.ContainsSingle(regexEngine.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, colorValue.Status);
        Assert.AreEqual(CliColorMode.Ansi, colorValue.LowArgs!.ColorMode);
        Assert.ContainsSingle(colorValue.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, generateValue.Status);
        Assert.AreEqual(CliGenerateMode.CompleteFish, generateValue.LowArgs!.GenerateMode);
        Assert.IsEmpty(generateValue.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, colorsValue.Status);
        Assert.AreSequenceEqual<string>(["path:none"], colorsValue.LowArgs!.ColorSpecs);
        Assert.ContainsSingle(colorsValue.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, sizeLimits.Status);
        Assert.AreEqual(9UL * 1024UL * 1024UL * 1024UL, sizeLimits.LowArgs!.DfaSizeLimit);
        Assert.AreEqual(2UL * 1024UL * 1024UL, sizeLimits.LowArgs.RegexSizeLimit);
        Assert.ContainsSingle(sizeLimits.LowArgs.Positional);
        Assert.AreEqual(CliParseStatus.Ok, contextValues.Status);
        Assert.AreEqual(2UL, contextValues.LowArgs!.AfterContext);
        Assert.AreEqual(3UL, contextValues.LowArgs.BeforeContext);
        Assert.AreEqual(5UL, contextValues.LowArgs.MaxDepth);
        Assert.ContainsSingle(contextValues.LowArgs.Positional);

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

        Assert.AreEqual(CliParseStatus.Ok, remainingValues.Status);
        Assert.ContainsSingle(remainingValues.LowArgs!.Patterns);
        Assert.AreSequenceEqual("bar"u8.ToArray(), remainingValues.LowArgs.Replacement!.Value.ToArray());
        Assert.HasCount(2, remainingValues.LowArgs.GlobPatterns);
        Assert.IsNotNull(remainingValues.LowArgs.SortMode);
        Assert.AreEqual(CliSortKind.LastModified, remainingValues.LowArgs.SortMode.Value.Kind);
        Assert.IsTrue(remainingValues.LowArgs.SortMode.Value.Reverse);
        Assert.Contains(static change => change.Kind == CliTypeChangeKind.Select && change.Value == "rust", remainingValues.LowArgs.TypeChanges);
        Assert.Contains(static change => change.Kind == CliTypeChangeKind.Negate && change.Value == "go", remainingValues.LowArgs.TypeChanges);
        Assert.Contains(static change => change.Kind == CliTypeChangeKind.Add && change.Value == "proto:*.proto", remainingValues.LowArgs.TypeChanges);
        Assert.Contains(static change => change.Kind == CliTypeChangeKind.Clear && change.Value == "py", remainingValues.LowArgs.TypeChanges);
        Assert.AreEqual(1024UL * 1024UL, remainingValues.LowArgs.MaxFileSize);
        Assert.AreSequenceEqual(":"u8.ToArray(), remainingValues.LowArgs.FieldMatchSeparator.ToArray());
        Assert.AreSequenceEqual("|"u8.ToArray(), remainingValues.LowArgs.FieldContextSeparator.ToArray());
        Assert.AreSequenceEqual("---"u8.ToArray(), remainingValues.LowArgs.ContextSeparator.ToArray());
        Assert.AreEqual((byte)'/', remainingValues.LowArgs.PathSeparator);
        Assert.AreSequenceEqual<string>([".ignore-extra"], remainingValues.LowArgs.IgnoreFiles);
        Assert.AreEqual("cat", remainingValues.LowArgs.Preprocessor);
        Assert.AreSequenceEqual<string>(["*.txt"], remainingValues.LowArgs.PreprocessorGlobs);
        Assert.AreEqual("hostname", remainingValues.LowArgs.HostnameBin);
        Assert.AreEqual(string.Empty, remainingValues.LowArgs.HyperlinkFormat);
    }

    /// <summary>
    /// Verifies parser special-mode behavior is routed through generated flag definitions.
    /// </summary>
    [TestMethod]
    public void ParserUsesGeneratedSpecialDefinitions()
    {
        CliParseResult helpShort = CliParser.Parse([OsString.FromUnixBytes("-h"u8)]);
        CliParseResult helpLong = CliParser.Parse([OsString.FromUnixBytes("--help"u8)]);
        CliParseResult versionShort = CliParser.Parse([OsString.FromUnixBytes("-V"u8)]);
        CliParseResult versionLong = CliParser.Parse([OsString.FromUnixBytes("--version"u8)]);
        CliParseResult pcre2Version = CliParser.Parse([OsString.FromUnixBytes("--pcre2-version"u8)]);
        CliParseResult clusteredHelp = CliParser.Parse([OsString.FromUnixBytes("-nh"u8)]);

        Assert.AreEqual(CliParseStatus.Special, helpShort.Status);
        Assert.AreEqual(CliSpecialMode.HelpShort, helpShort.SpecialMode);
        Assert.AreEqual(CliParseStatus.Special, helpLong.Status);
        Assert.AreEqual(CliSpecialMode.HelpLong, helpLong.SpecialMode);
        Assert.AreEqual(CliParseStatus.Special, versionShort.Status);
        Assert.AreEqual(CliSpecialMode.VersionShort, versionShort.SpecialMode);
        Assert.AreEqual(CliParseStatus.Special, versionLong.Status);
        Assert.AreEqual(CliSpecialMode.VersionLong, versionLong.SpecialMode);
        Assert.AreEqual(CliParseStatus.Special, pcre2Version.Status);
        Assert.AreEqual(CliSpecialMode.Pcre2Version, pcre2Version.SpecialMode);
        Assert.AreEqual(CliParseStatus.Special, clusteredHelp.Status);
        Assert.AreEqual(CliSpecialMode.HelpShort, clusteredHelp.SpecialMode);
    }

    /// <summary>
    /// Verifies spelling-sensitive generated switch diagnostics match ripgrep wording.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(CliParseStatus.Error, shortError.Status);
        Assert.AreEqual("error parsing flag -u: flag can only be repeated up to 3 times", shortError.Error!.FormatAlternate());
        Assert.AreEqual(CliParseStatus.Error, longError.Status);
        Assert.AreEqual("error parsing flag --unrestricted: flag can only be repeated up to 3 times", longError.Error!.FormatAlternate());
    }
}
