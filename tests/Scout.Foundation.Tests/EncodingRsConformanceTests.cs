using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Scout;

/// <summary>
/// Verifies Scout decoders against the pinned <c>encoding_rs</c> decode vectors.
/// </summary>
public sealed class EncodingRsConformanceTests
{
    private const string EncodingRsTestDataRoot = "/Users/brandon/.cargo/registry/src/index.crates.io-1949cf8c6b5b557f/encoding_rs-0.8.35/src/test_data";

    /// <summary>
    /// Gets the upstream decode-vector cases Scout currently supports.
    /// </summary>
    public static IEnumerable<object[]> DecodeVectorCases()
    {
        yield return [SearchEncodingKind.Big5, "big5_in.txt", "big5_in_ref.txt"];
        yield return [SearchEncodingKind.EucKr, "euc_kr_in.txt", "euc_kr_in_ref.txt"];
        yield return [SearchEncodingKind.Gb18030, "gb18030_in.txt", "gb18030_in_ref.txt"];
        yield return [SearchEncodingKind.Iso2022Jp, "iso_2022_jp_in.txt", "iso_2022_jp_in_ref.txt"];
        yield return [SearchEncodingKind.EucJp, "jis0208_in.txt", "jis0208_in_ref.txt"];
        yield return [SearchEncodingKind.EucJp, "jis0212_in.txt", "jis0212_in_ref.txt"];
        yield return [SearchEncodingKind.ShiftJis, "shift_jis_in.txt", "shift_jis_in_ref.txt"];
    }

    /// <summary>
    /// Verifies the conformance catalog covers every upstream decode vector.
    /// </summary>
    [Fact]
    public void DecodeVectorCatalogTracksEncodingRsTestData()
    {
        var upstream = new List<string>();
        foreach (string path in Directory.EnumerateFiles(EncodingRsTestDataRoot, "*_in.txt"))
        {
            upstream.Add(Path.GetFileName(path));
        }

        upstream.Sort(StringComparer.Ordinal);

        Assert.Equal(
            [
                "big5_in.txt",
                "euc_kr_in.txt",
                "gb18030_in.txt",
                "iso_2022_jp_in.txt",
                "jis0208_in.txt",
                "jis0212_in.txt",
                "shift_jis_in.txt",
            ],
            upstream);
    }

    /// <summary>
    /// Verifies the upstream decode-vector files are pinned by hash.
    /// </summary>
    [Fact]
    public void DecodeVectorFilesMatchPinnedHashes()
    {
        (string Name, string FileName, string Sha256)[] vectors =
        [
            ("encoding-rs-0.8.35-big5-in", "big5_in.txt", "A5AE290786610C7FACDBB1D06BE6815E8BB81D68DFA7380EDC7DDB6B8C7E412E"),
            ("encoding-rs-0.8.35-euc-kr-in", "euc_kr_in.txt", "21534EC87E82D785D128F980902D13FAB6D9DEA15C69A991F409D9B0AFD1C852"),
            ("encoding-rs-0.8.35-gb18030-in", "gb18030_in.txt", "0DD1FBF0360930DAAFDCE7E6761852005A8F92F7910180DF19A070B6C3C59DEE"),
            ("encoding-rs-0.8.35-iso-2022-jp-in", "iso_2022_jp_in.txt", "98FB823530A30A76EB0BA7AB6AC796959E8869C1EC143F018A1964581AE813AA"),
            ("encoding-rs-0.8.35-jis0208-in", "jis0208_in.txt", "3C1A7AAADA00D6FFF41E60BD7589D7AE94AFAF562759B116958B1D84711D17B6"),
            ("encoding-rs-0.8.35-jis0212-in", "jis0212_in.txt", "9EDA766002646A27310457C661832429BEFF9089D2703355812C4CAC30D800B5"),
            ("encoding-rs-0.8.35-shift-jis-in", "shift_jis_in.txt", "E65DF746BE90359E70422868522A7AA3E8ED20E0D4C0CF9995D9DE248BF93388"),
            ("encoding-rs-0.8.35-big5-in-ref", "big5_in_ref.txt", "52733D9970FB8987F014FA0EAC2792250123CF583B363BEB2CE7B3C55E3DC555"),
            ("encoding-rs-0.8.35-euc-kr-in-ref", "euc_kr_in_ref.txt", "B3009E6A94967DF1F1135C66F005610BEBAD6666D634AEC72D2AD77C3211BDA3"),
            ("encoding-rs-0.8.35-gb18030-in-ref", "gb18030_in_ref.txt", "F3EAEA1115857054CF30F8BA18219E72377F2A4AFD848CCF223770D75AA4251D"),
            ("encoding-rs-0.8.35-iso-2022-jp-in-ref", "iso_2022_jp_in_ref.txt", "DF82166D2E01BB446211D7E46F33B47D93AC1BAFEAE2ED880EE93C4096D11210"),
            ("encoding-rs-0.8.35-jis0208-in-ref", "jis0208_in_ref.txt", "DF82166D2E01BB446211D7E46F33B47D93AC1BAFEAE2ED880EE93C4096D11210"),
            ("encoding-rs-0.8.35-jis0212-in-ref", "jis0212_in_ref.txt", "FCF74C58D2CBE9B3F3DB4FA9EFF26E09F5E9644097C78614C03177E938988D03"),
            ("encoding-rs-0.8.35-shift-jis-in-ref", "shift_jis_in_ref.txt", "13B15AED64E9E7CB35E6A740B67413F0C285FDA944F4814D45A7619EA338FB5E"),
        ];

        string prerequisiteLock = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tests", "PREREQS.lock"));
        for (int index = 0; index < vectors.Length; index++)
        {
            (string name, string fileName, string expectedSha256) = vectors[index];
            Assert.Contains("name = \"" + name + "\"", prerequisiteLock, StringComparison.Ordinal);
            Assert.Contains("sha256 = \"" + expectedSha256.ToLowerInvariant() + "\"", prerequisiteLock, StringComparison.Ordinal);

            byte[] hash = SHA256.HashData(File.ReadAllBytes(Path.Combine(EncodingRsTestDataRoot, fileName)));
            Assert.Equal(expectedSha256, Convert.ToHexString(hash));
        }
    }

    /// <summary>
    /// Verifies Scout decoding matches upstream <c>encoding_rs</c> reference output exactly.
    /// </summary>
    /// <param name="encodingKind">The Scout encoding kind under test.</param>
    /// <param name="inputFile">The upstream encoded input vector.</param>
    /// <param name="expectedFile">The upstream UTF-8 reference vector.</param>
    [Theory]
    [MemberData(nameof(DecodeVectorCases))]
    public void DecodeMatchesEncodingRsReferenceOutput(SearchEncodingKind encodingKind, string inputFile, string expectedFile)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(EncodingRsTestDataRoot, inputFile));
        byte[] expected = File.ReadAllBytes(Path.Combine(EncodingRsTestDataRoot, expectedFile));

        byte[] actual = SearchEncoding.Decode(input, encodingKind);

        Assert.Equal(expected, actual);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Scout.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Scout repository root.");
    }
}
