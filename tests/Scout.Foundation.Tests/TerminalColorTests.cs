
namespace Scout;

/// <summary>
/// Verifies terminal color mode resolution follows termcolor-compatible environment rules.
/// </summary>
public sealed class TerminalColorTests
{
    /// <summary>
    /// Verifies automatic color is disabled when stdout is not a terminal.
    /// </summary>
    [Fact]
    public void AutoColorRequiresTerminalOutput()
    {
        Assert.False(TerminalColor.ShouldEnableAutoColor(false, _ => "xterm-256color", isWindows: false));
        Assert.Equal(
            CliColorMode.Auto,
            TerminalColor.Resolve(CliColorMode.Auto, standardOutputIsTerminal: false, _ => "xterm-256color", isWindows: false));
    }

    /// <summary>
    /// Verifies Unix automatic color follows termcolor's TERM and NO_COLOR handling.
    /// </summary>
    [Fact]
    public void AutoColorMatchesTermcolorUnixEnvironmentRules()
    {
        Assert.False(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(), isWindows: false));
        Assert.False(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "dumb")), isWindows: false));
        Assert.False(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "xterm-256color"), ("NO_COLOR", string.Empty)), isWindows: false));
        Assert.True(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "xterm-256color")), isWindows: false));
    }

    /// <summary>
    /// Verifies Windows automatic color permits an absent TERM while honoring TERM=dumb and NO_COLOR.
    /// </summary>
    [Fact]
    public void AutoColorMatchesTermcolorWindowsEnvironmentRules()
    {
        Assert.True(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(), isWindows: true));
        Assert.False(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "dumb")), isWindows: true));
        Assert.False(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("NO_COLOR", string.Empty)), isWindows: true));
        Assert.True(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "xterm-256color")), isWindows: true));
    }

    /// <summary>
    /// Verifies automatic color resolves to ANSI output only when terminal and environment checks allow color.
    /// </summary>
    [Fact]
    public void AutoColorResolvesToAnsiOnlyWhenEnvironmentAllowsIt()
    {
        Assert.Equal(
            CliColorMode.Ansi,
            TerminalColor.Resolve(CliColorMode.Auto, standardOutputIsTerminal: true, UnixEnvironment(("TERM", "xterm-256color")), isWindows: false));
        Assert.Equal(
            CliColorMode.Auto,
            TerminalColor.Resolve(CliColorMode.Auto, standardOutputIsTerminal: true, UnixEnvironment(("TERM", "dumb")), isWindows: false));
    }

    /// <summary>
    /// Verifies explicit color choices bypass automatic environment checks.
    /// </summary>
    [Fact]
    public void ExplicitColorModesDoNotConsultTheEnvironment()
    {
        Func<string, string?> throwingEnvironment = _ => throw new InvalidOperationException("environment should not be read");

        Assert.Equal(CliColorMode.Always, TerminalColor.Resolve(CliColorMode.Always, standardOutputIsTerminal: false, throwingEnvironment, isWindows: false));
        Assert.Equal(CliColorMode.Ansi, TerminalColor.Resolve(CliColorMode.Ansi, standardOutputIsTerminal: false, throwingEnvironment, isWindows: false));
        Assert.Equal(CliColorMode.Never, TerminalColor.Resolve(CliColorMode.Never, standardOutputIsTerminal: true, throwingEnvironment, isWindows: false));
    }

    private static Func<string, string?> UnixEnvironment(params (string Name, string? Value)[] variables)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int index = 0; index < variables.Length; index++)
        {
            map[variables[index].Name] = variables[index].Value;
        }

        return name => map.TryGetValue(name, out string? value) ? value : null;
    }
}
