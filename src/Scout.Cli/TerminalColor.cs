
namespace Scout;

internal static class TerminalColor
{
    internal static CliColorMode Resolve(CliColorMode colorMode, bool standardOutputIsTerminal)
    {
        return Resolve(colorMode, standardOutputIsTerminal, ProcessEnvironment.GetVariable, OperatingSystem.IsWindows());
    }

    internal static CliColorMode Resolve(
        CliColorMode colorMode,
        bool standardOutputIsTerminal,
        Func<string, string?> getVariable,
        bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        if (colorMode != CliColorMode.Auto)
        {
            return colorMode;
        }

        return ShouldEnableAutoColor(standardOutputIsTerminal, getVariable, isWindows)
            ? CliColorMode.Ansi
            : CliColorMode.Auto;
    }

    internal static bool ShouldEnableAutoColor(
        bool standardOutputIsTerminal,
        Func<string, string?> getVariable,
        bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        if (!standardOutputIsTerminal)
        {
            return false;
        }

        if (string.Equals(getVariable("TERM"), "dumb", StringComparison.Ordinal))
        {
            return false;
        }

        if (!isWindows && getVariable("TERM") is null)
        {
            return false;
        }

        return getVariable("NO_COLOR") is null;
    }
}
