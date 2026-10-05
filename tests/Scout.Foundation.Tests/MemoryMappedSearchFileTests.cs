namespace Scout;

/// <summary>
/// Verifies zero-copy mapped search-file ownership and lifetime behavior.
/// </summary>
[TestClass]
public sealed class MemoryMappedSearchFileTests
{
    /// <summary>
    /// Verifies a non-empty file is exposed directly until its mapping is disposed.
    /// </summary>
    [TestMethod]
    public void TryOpenExposesMappedBytesUntilDisposed()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "input.txt");
            File.WriteAllBytes(path, "alpha\nneedle\n"u8.ToArray());

            Assert.IsTrue(MemoryMappedSearchFile.TryOpen(path, out MemoryMappedSearchFile? mappedSearchFile));
            Assert.IsNotNull(mappedSearchFile);
            Assert.IsTrue(mappedSearchFile.Bytes.SequenceEqual("alpha\nneedle\n"u8));

            mappedSearchFile.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => mappedSearchFile.Bytes.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an empty file declines mapping so the buffered empty-input path remains authoritative.
    /// </summary>
    [TestMethod]
    public void TryOpenDeclinesEmptyFile()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "empty.txt");
            File.WriteAllBytes(path, []);

            using (var owner = new DisposableOwner<MemoryMappedSearchFile>())
            {
                Assert.IsFalse(MemoryMappedSearchFile.TryOpen(path, out owner.Resource));
                Assert.IsNull(owner.Resource);
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
    [TestMethod]
    public void TryMapViewReplacesCurrentView()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "input.txt");
            File.WriteAllBytes(path, "0123456789"u8.ToArray());

            Assert.IsTrue(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.IsNotNull(mappedSearchFile);
                Assert.AreEqual(10, mappedSearchFile.Length);
                Assert.IsTrue(mappedSearchFile.TryMapView(offset: 0, maximumLength: 4));
                Assert.IsTrue(mappedSearchFile.Bytes.SequenceEqual("0123"u8));
                Assert.IsTrue(mappedSearchFile.TryMapView(offset: 4, maximumLength: 4));
                Assert.IsTrue(mappedSearchFile.Bytes.SequenceEqual("4567"u8));
                Assert.IsTrue(mappedSearchFile.TryMapView(offset: 8, maximumLength: 4));
                Assert.IsTrue(mappedSearchFile.Bytes.SequenceEqual("89"u8));
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
    [TestMethod]
    [DataRow(CliSearchMode.Count)]
    [DataRow(CliSearchMode.CountMatches)]
    public void BoundedCountHandlesGeneralExpressionsAcrossViews(CliSearchMode searchMode)
    {
        string root = CreateTempDirectory();
        try
        {
            const int repeatedRecords = 180_000;
            string path = Path.Join(root, "input.txt");
            WriteRepeatedRecords(
                path,
                "alpha bravo charl delta echoo foxtt\r\n"u8.ToArray(),
                repeatedRecords,
                "alpha bravo charl delta echoo foxtt"u8.ToArray());
            using RegexSpecializationModeScope scope =
                RegexSpecializationModeDefaults.Use(RegexSpecializationMode.General);

            Assert.IsTrue(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.IsNotNull(mappedSearchFile);
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
                    Assert.IsTrue(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
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

                    Assert.AreEqual(expectedCount, count);
                    Assert.IsFalse(containsNul);
                    Assert.IsNotNull(regexPlan);
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
    [TestMethod]
    [DataRow(CliSearchMode.Count, 1)]
    [DataRow(CliSearchMode.CountMatches, 2)]
    public void BoundedCountReportsLateNul(
        CliSearchMode searchMode,
        int matchesPerRecord)
    {
        string root = CreateTempDirectory();
        try
        {
            const int repeatedRecords = 180_000;
            string path = Path.Join(root, "input.txt");
            WriteRepeatedRecords(
                path,
                "alpha bravo charl delta echoo foxtt\r\n"u8.ToArray(),
                repeatedRecords,
                "alpha bravo charl delta echoo foxtt\0"u8.ToArray());
            byte[][] patterns = [@"\b\w{5}\s+\w{5}\s+\w{5}\b"u8.ToArray()];
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                new RegexSearchPlanOptions(asciiCaseInsensitive: false, crlf: true));

            Assert.IsTrue(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.IsNotNull(mappedSearchFile);
                Assert.IsTrue(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
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
                Assert.AreEqual((repeatedRecords + 1L) * matchesPerRecord, count);
                Assert.IsTrue(containsNul);
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
    [TestMethod]
    public void BoundedMatchCountFusesCommonPrefixLiteralsAndLateNul()
    {
        string root = CreateTempDirectory();
        try
        {
            const int repeatedRecords = 220_000;
            string path = Path.Join(root, "input.txt");
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

            Assert.IsTrue(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.IsNotNull(mappedSearchFile);
                Assert.IsTrue(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
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
                Assert.AreEqual(1, count);
                Assert.IsTrue(containsNul);
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
    [TestMethod]
    public void BoundedCountDeclinesOversizedRecord()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "input.txt");
            File.WriteAllBytes(path, new byte[(8 * 1024 * 1024) + 1]);
            byte[][] patterns = ["needle"u8.ToArray()];
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                asciiCaseInsensitive: false);

            Assert.IsTrue(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.IsNotNull(mappedSearchFile);
                Assert.IsFalse(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
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
    [TestMethod]
    [DataRow("a*", CliSearchMode.Count)]
    [DataRow("a*", CliSearchMode.CountMatches)]
    [DataRow(@"\Afoo", CliSearchMode.Count)]
    [DataRow(@"\Afoo", CliSearchMode.CountMatches)]
    public void BoundedCountDeclinesBoundaryDependentExpression(
        string pattern,
        CliSearchMode searchMode)
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "input.txt");
            File.WriteAllBytes(path, "foo\n"u8.ToArray());
            byte[][] patterns = [System.Text.Encoding.UTF8.GetBytes(pattern)];
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                asciiCaseInsensitive: false);

            Assert.IsTrue(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.IsNotNull(mappedSearchFile);
                Assert.IsFalse(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
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
                Assert.IsNotNull(regexPlan);
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
    [TestMethod]
    [DataRow(CliSearchMode.Count)]
    [DataRow(CliSearchMode.CountMatches)]
    public void BoundedCountDeclinesMultilineSearch(CliSearchMode searchMode)
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Join(root, "input.txt");
            File.WriteAllBytes(path, "foo\nbar\n"u8.ToArray());
            byte[][] patterns = ["foo.*bar"u8.ToArray()];
            var regexPlan = RegexSearchPlan.Create(
                patterns,
                asciiCaseInsensitive: false);

            Assert.IsTrue(MemoryMappedSearchFile.TryOpenFile(
                path,
                out MemoryMappedSearchFile? mappedSearchFile));
            using (mappedSearchFile)
            {
                Assert.IsNotNull(mappedSearchFile);
                Assert.IsFalse(StandardSearchTargetOperations.TryCountMemoryMappedWindows(
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
        string root = Path.Join(Path.GetTempPath(), $"scout-mmap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
