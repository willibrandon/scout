using System;
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
            "\nsimd(compile):+NEON\nsimd(runtime):+NEON\n\n" +
            Pcre2Library.VersionText +
            "\n");
    }
}
