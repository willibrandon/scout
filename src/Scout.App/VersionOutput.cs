using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Scout;

internal static class VersionOutput
{
    internal static ReadOnlySpan<byte> Short => "ripgrep 15.1.0 (rev 4857d6fa67)\n"u8;

    internal static byte[] GetLong()
    {
        return Encoding.ASCII.GetBytes(
            "ripgrep 15.1.0 (rev 4857d6fa67)\n\nfeatures:" +
            Pcre2Library.FeatureLabel +
            GetSimdLines() +
            "\n\n" +
            Pcre2Library.VersionText +
            "\n");
    }

    private static string GetSimdLines()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "\nsimd(compile):+SSE2,-SSSE3,-AVX2\nsimd(runtime):" +
                JoinFeatureSigns(
                    ("SSE2", Sse2.IsSupported),
                    ("SSSE3", Ssse3.IsSupported),
                    ("AVX2", Avx2.IsSupported)),
            Architecture.Arm64 => "\nsimd(compile):+NEON\nsimd(runtime):+NEON",
            _ => string.Empty,
        };
    }

    private static string JoinFeatureSigns(params (string Name, bool Enabled)[] features)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < features.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(features[index].Enabled ? '+' : '-');
            builder.Append(features[index].Name);
        }

        return builder.ToString();
    }
}
