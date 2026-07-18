namespace Scout;

/// <summary>
/// Verifies zero-copy mapped search-file ownership and lifetime behavior.
/// </summary>
public sealed class MemoryMappedSearchFileTests
{
    /// <summary>
    /// Verifies a non-empty file is exposed directly until its mapping is disposed.
    /// </summary>
    [Fact]
    public void TryOpenExposesMappedBytesUntilDisposed()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllBytes(path, "alpha\nneedle\n"u8.ToArray());

            Assert.True(MemoryMappedSearchFile.TryOpen(path, out MemoryMappedSearchFile? mappedSearchFile));
            Assert.NotNull(mappedSearchFile);
            Assert.True(mappedSearchFile.Bytes.SequenceEqual("alpha\nneedle\n"u8));

            mappedSearchFile.Dispose();

            Assert.Throws<ObjectDisposedException>(() => mappedSearchFile.Bytes.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an empty file declines mapping so the buffered empty-input path remains authoritative.
    /// </summary>
    [Fact]
    public void TryOpenDeclinesEmptyFile()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "empty.txt");
            File.WriteAllBytes(path, []);

            MemoryMappedSearchFile? mappedSearchFile = null;
            try
            {
                Assert.False(MemoryMappedSearchFile.TryOpen(path, out mappedSearchFile));
                Assert.Null(mappedSearchFile);
            }
            finally
            {
                mappedSearchFile?.Dispose();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies bounded views can advance without retaining the preceding mapped pages.
    /// </summary>
    [Fact]
    public void TryMapViewReplacesCurrentView()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllBytes(path, "0123456789"u8.ToArray());

            Assert.True(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.NotNull(mappedSearchFile);
                Assert.Equal(10, mappedSearchFile.Length);
                Assert.True(mappedSearchFile.TryMapView(offset: 0, maximumLength: 4));
                Assert.True(mappedSearchFile.Bytes.SequenceEqual("0123"u8));
                Assert.True(mappedSearchFile.TryMapView(offset: 4, maximumLength: 4));
                Assert.True(mappedSearchFile.Bytes.SequenceEqual("4567"u8));
                Assert.True(mappedSearchFile.TryMapView(offset: 8, maximumLength: 4));
                Assert.True(mappedSearchFile.Bytes.SequenceEqual("89"u8));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies bounded CRLF views preserve record and match counts for general and line-anchored
    /// expressions, including a final record without a terminator.
    /// </summary>
    /// <param name="searchMode">Whether matching records or individual matches are counted.</param>
    [Theory]
    [InlineData(CliSearchMode.Count)]
    [InlineData(CliSearchMode.CountMatches)]
    public void BoundedCountHandlesGeneralExpressionsAcrossViews(CliSearchMode searchMode)
    {
        string root = CreateTempDirectory();
        try
        {
            const int repeatedRecords = 180_000;
            string path = Path.Combine(root, "input.txt");
            WriteRepeatedRecords(
                path,
                "alpha bravo charl delta echoo foxtt\r\n"u8.ToArray(),
                repeatedRecords,
                "alpha bravo charl delta echoo foxtt"u8.ToArray());
            using RegexSpecializationModeScope scope =
                RegexSpecializationModeDefaults.Use(RegexSpecializationMode.General);

            Assert.True(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.NotNull(mappedSearchFile);
                var cases = new (byte[] Pattern, int MatchesPerRecord)[]
                {
                    (@"\b\w{5}\s+\w{5}\s+\w{5}\b"u8.ToArray(), 2),
                    ("^alpha bravo charl delta echoo foxtt$"u8.ToArray(), 1),
                };
                foreach ((byte[] pattern, int matchesPerRecord) in cases)
                {
                    byte[][] patterns = [pattern];
                    var regexPlan = RegexSearchPlan.Create(
                        patterns,
                        new RegexSearchPlanOptions(asciiCaseInsensitive: false, crlf: true));
                    Assert.True(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
                        mappedSearchFile,
                        patterns,
                        regexPlan,
                        searchMode,
                        asciiCaseInsensitive: false,
                        invertMatch: false,
                        lineRegexp: false,
                        wordRegexp: false,
                        crlf: true,
                        multiline: false,
                        multilineDotall: false,
                        out long count,
                        out bool containsNul));
                    long expectedCount = repeatedRecords + 1L;
                    if (searchMode == CliSearchMode.CountMatches)
                    {
                        expectedCount *= matchesPerRecord;
                    }

                    Assert.Equal(expectedCount, count);
                    Assert.False(containsNul);
                    Assert.NotNull(regexPlan);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies bounded counting reports a NUL found after earlier views together with the complete
    /// mode-specific count.
    /// </summary>
    /// <param name="searchMode">Whether matching records or individual matches are counted.</param>
    /// <param name="matchesPerRecord">The expected contribution from each matching record.</param>
    [Theory]
    [InlineData(CliSearchMode.Count, 1)]
    [InlineData(CliSearchMode.CountMatches, 2)]
    public void BoundedCountReportsLateNul(
        CliSearchMode searchMode,
        int matchesPerRecord)
    {
        string root = CreateTempDirectory();
        try
        {
            const int repeatedRecords = 180_000;
            string path = Path.Combine(root, "input.txt");
            WriteRepeatedRecords(
                path,
                "alpha bravo charl delta echoo foxtt\r\n"u8.ToArray(),
                repeatedRecords,
                "alpha bravo charl delta echoo foxtt\0"u8.ToArray());
            byte[][] patterns = [@"\b\w{5}\s+\w{5}\s+\w{5}\b"u8.ToArray()];
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                new RegexSearchPlanOptions(asciiCaseInsensitive: false, crlf: true));

            Assert.True(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.NotNull(mappedSearchFile);
                Assert.True(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
                    mappedSearchFile,
                    patterns,
                    regexPlan,
                    searchMode,
                    asciiCaseInsensitive: false,
                    invertMatch: false,
                    lineRegexp: false,
                    wordRegexp: false,
                    crlf: true,
                    multiline: false,
                    multilineDotall: false,
                    out long count,
                    out bool containsNul));
                Assert.Equal((repeatedRecords + 1L) * matchesPerRecord, count);
                Assert.True(containsNul);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies bounded mapped counting carries an exact common-prefix literal set across views
    /// while its authoritative candidate scan reports a late NUL.
    /// </summary>
    [Fact]
    public void BoundedMatchCountFusesCommonPrefixLiteralsAndLateNul()
    {
        string root = CreateTempDirectory();
        try
        {
            const int repeatedRecords = 220_000;
            string path = Path.Combine(root, "input.txt");
            WriteRepeatedRecords(
                path,
                "ordinary source text\n"u8.ToArray(),
                repeatedRecords,
                "issue44_absent_pattern_063\0"u8.ToArray());
            byte[][] patterns = Enumerable.Range(0, 64)
                .Select(static index =>
                    System.Text.Encoding.ASCII.GetBytes(
                        $"issue44_absent_pattern_{index:D3}"))
                .ToArray();
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                asciiCaseInsensitive: false);

            Assert.True(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.NotNull(mappedSearchFile);
                Assert.True(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
                    mappedSearchFile,
                    patterns,
                    regexPlan,
                    CliSearchMode.CountMatches,
                    asciiCaseInsensitive: false,
                    invertMatch: false,
                    lineRegexp: false,
                    wordRegexp: false,
                    crlf: false,
                    multiline: false,
                    multilineDotall: false,
                    out long count,
                    out bool containsNul));
                Assert.Equal(1, count);
                Assert.True(containsNul);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a record larger than the bounded carry limit declines the optimization before
    /// unbounded memory is retained.
    /// </summary>
    [Fact]
    public void BoundedCountDeclinesOversizedRecord()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllBytes(path, new byte[(8 * 1024 * 1024) + 1]);
            byte[][] patterns = ["needle"u8.ToArray()];
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                asciiCaseInsensitive: false);

            Assert.True(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.NotNull(mappedSearchFile);
                Assert.False(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
                    mappedSearchFile,
                    patterns,
                    regexPlan,
                    CliSearchMode.CountMatches,
                    asciiCaseInsensitive: false,
                    invertMatch: false,
                    lineRegexp: false,
                    wordRegexp: false,
                    crlf: false,
                    multiline: false,
                    multilineDotall: false,
                    out _,
                    out _));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies record-boundary-dependent expressions decline bounded segmentation.
    /// </summary>
    /// <param name="pattern">The boundary-dependent expression.</param>
    /// <param name="searchMode">Whether matching records or individual matches would be counted.</param>
    [Theory]
    [InlineData("a*", CliSearchMode.Count)]
    [InlineData("a*", CliSearchMode.CountMatches)]
    [InlineData(@"\Afoo", CliSearchMode.Count)]
    [InlineData(@"\Afoo", CliSearchMode.CountMatches)]
    public void BoundedCountDeclinesBoundaryDependentExpression(
        string pattern,
        CliSearchMode searchMode)
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllBytes(path, "foo\n"u8.ToArray());
            byte[][] patterns = [System.Text.Encoding.UTF8.GetBytes(pattern)];
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                asciiCaseInsensitive: false);

            Assert.True(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.NotNull(mappedSearchFile);
                Assert.False(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
                    mappedSearchFile,
                    patterns,
                    regexPlan,
                    searchMode,
                    asciiCaseInsensitive: false,
                    invertMatch: false,
                    lineRegexp: false,
                    wordRegexp: false,
                    crlf: false,
                    multiline: false,
                    multilineDotall: false,
                    out _,
                    out _));
                Assert.NotNull(regexPlan);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies multiline semantics decline record-aligned bounded counting before a view is read.
    /// </summary>
    /// <param name="searchMode">Whether matching records or individual matches would be counted.</param>
    [Theory]
    [InlineData(CliSearchMode.Count)]
    [InlineData(CliSearchMode.CountMatches)]
    public void BoundedCountDeclinesMultilineSearch(CliSearchMode searchMode)
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "input.txt");
            File.WriteAllBytes(path, "foo\nbar\n"u8.ToArray());
            byte[][] patterns = ["foo.*bar"u8.ToArray()];
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                asciiCaseInsensitive: false);

            Assert.True(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.NotNull(mappedSearchFile);
                Assert.False(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
                    mappedSearchFile,
                    patterns,
                    regexPlan,
                    searchMode,
                    asciiCaseInsensitive: false,
                    invertMatch: false,
                    lineRegexp: false,
                    wordRegexp: false,
                    crlf: false,
                    multiline: true,
                    multilineDotall: true,
                    out _,
                    out _));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteRepeatedRecords(
        string path,
        byte[] record,
        int count,
        byte[]? finalRecord)
    {
        const int RecordsPerBlock = 4096;
        byte[] block = GC.AllocateUninitializedArray<byte>(record.Length * RecordsPerBlock);
        for (int index = 0; index < RecordsPerBlock; index++)
        {
            record.CopyTo(block.AsSpan(index * record.Length));
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        while (count > 0)
        {
            int records = Math.Min(count, RecordsPerBlock);
            stream.Write(block.AsSpan(0, records * record.Length));
            count -= records;
        }

        if (finalRecord is not null)
        {
            stream.Write(finalRecord);
        }
    }

    private static string CreateTempDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), $"scout-mmap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
