namespace Scout;

/// <summary>
/// Hosts the managed Scout application behind real process standard streams for integration tests.
/// </summary>
internal static class Program
{
    private static int Main(string[] arguments)
    {
        var osArguments = new OsString[arguments.Length + 1];
        osArguments[0] = OsString.FromText("scout");
        for (int index = 0; index < arguments.Length; index++)
        {
            osArguments[index + 1] = OsString.FromText(arguments[index]);
        }

        RawByteWriter output = RawStandardStreams.OpenOutput();
        RawByteWriter error = RawStandardStreams.OpenError();
        return ScoutApplication.Run(osArguments, output, error);
    }
}
