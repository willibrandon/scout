using System;

namespace Scout;

internal static class VersionOutput
{
    internal static ReadOnlySpan<byte> Short => "ripgrep 15.1.0 (rev 4857d6fa67)\n"u8;

    internal static ReadOnlySpan<byte> Long =>
        "ripgrep 15.1.0 (rev 4857d6fa67)\n\nfeatures:-pcre2\nsimd(compile):+NEON\nsimd(runtime):+NEON\n\nPCRE2 is not available in this build of ripgrep.\n\n"u8;

    internal static ReadOnlySpan<byte> Pcre2Unavailable =>
        "PCRE2 is not available in this build of ripgrep.\n"u8;
}
