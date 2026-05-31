using System.Runtime.InteropServices;

namespace Scout;

internal static unsafe class ScoutEntry
{
    [UnmanagedCallersOnly(EntryPoint = "scout_entry")]
    public static int Run(int argc, byte** argv, byte** envp)
    {
        if (!OperatingSystem.IsWindows())
        {
            ProcessEnvironment.UseUnixEnvironment(envp);
        }

        OsString[] arguments = OperatingSystem.IsWindows()
            ? NativeArgumentReader.CaptureWindowsCommandLine()
            : NativeArgumentReader.CaptureUnix(argc, argv);
        return ScoutApplication.Run(arguments, RawStandardStreams.OpenOutput(), RawStandardStreams.OpenError());
    }
}
