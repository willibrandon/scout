using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

/// <summary>
/// Describes the PCRE2 runtime linked into the current Scout build.
/// </summary>
public static unsafe partial class Pcre2Library
{
    private const uint ConfigJit = 1;
    private const uint ConfigVersion = 11;
    private const int MaxVersionLength = 64;
    private const string UnavailableVersionText = "PCRE2 is not available in this build of ripgrep.\n";

    /// <summary>
    /// Gets a value indicating whether this build has a linked PCRE2 runtime.
    /// </summary>
    public static bool IsAvailable => TryGetConfigUInt32(ConfigJit, out _);

    /// <summary>
    /// Gets a value indicating whether the linked PCRE2 runtime reports JIT support.
    /// </summary>
    public static bool IsJitAvailable => TryGetConfigUInt32(ConfigJit, out uint value) && value == 1;

    /// <summary>
    /// Gets the ripgrep-compatible error message used when PCRE2 is requested but not linked.
    /// </summary>
    public const string UnavailableErrorMessage = "PCRE2 is not available in this build of ripgrep";

    /// <summary>
    /// Gets the <c>--pcre2-version</c> output for a build without PCRE2.
    /// </summary>
    public static ReadOnlySpan<byte> UnavailableVersionOutput =>
        "PCRE2 is not available in this build of ripgrep.\n"u8;

    /// <summary>
    /// Gets the <c>--pcre2-version</c> output for the current runtime.
    /// </summary>
    public static byte[] GetVersionOutput()
    {
        return Encoding.ASCII.GetBytes(VersionText);
    }

    /// <summary>
    /// Gets the ripgrep feature label for PCRE2 in <c>--version</c> output.
    /// </summary>
    public static string FeatureLabel => IsAvailable ? "+pcre2" : "-pcre2";

    /// <summary>
    /// Gets the textual PCRE2 version summary for the current runtime.
    /// </summary>
    public static string VersionText
    {
        get
        {
            if (!IsAvailable)
            {
                return UnavailableVersionText;
            }

            string jit = IsJitAvailable ? "available" : "unavailable";
            string version = TryGetVersionNumber(out string runtimeVersion)
                ? runtimeVersion
                : "unknown";
            return "PCRE2 " + version + " is available (JIT is " + jit + ")\n";
        }
    }

    private static bool TryGetVersionNumber(out string version)
    {
        version = string.Empty;
        try
        {
            Span<byte> buffer = stackalloc byte[MaxVersionLength];
            fixed (byte* bufferPointer = buffer)
            {
                int length = Pcre2Config(ConfigVersion, bufferPointer);
                if (length <= 1 || length > MaxVersionLength)
                {
                    return false;
                }

                ReadOnlySpan<byte> text = buffer[..(length - 1)];
                int space = text.IndexOf((byte)' ');
                if (space > 0)
                {
                    text = text[..space];
                }

                version = Encoding.ASCII.GetString(text);
                return version.Length > 0;
            }
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool TryGetConfigUInt32(uint what, out uint value)
    {
        value = 0;
        try
        {
            uint result = 0;
            bool success = Pcre2Config(what, &result) >= 0;
            value = result;
            return success;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_config_8")]
    private static partial int Pcre2Config(uint what, void* where);
}
