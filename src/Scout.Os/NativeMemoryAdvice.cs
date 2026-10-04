using System.Runtime.InteropServices;

namespace Scout;

/// <summary>
/// Provides the upstream Unix sequential-access hint for live memory mappings.
/// </summary>
internal static unsafe partial class NativeMemoryAdvice
{
    internal static bool TrySequential(byte* address, nuint length, out int error, Func<nint, nuint, int>? advise = null)
    {
        if ((!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) || length == 0)
        {
            error = 0;
            return true;
        }

        if (advise is not null)
        {
            error = advise((nint)address, length);
            return error == 0;
        }

        int result = Advise(address, length, advice: 2);
        error = result == 0 ? 0 : Marshal.GetLastPInvokeError();
        return result == 0;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "madvise", SetLastError = true)]
    private static partial int Advise(byte* address, nuint length, int advice);
}
