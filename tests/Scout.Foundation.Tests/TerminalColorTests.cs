
namespace Scout;

/// <summary>
/// Verifies terminal color mode resolution follows termcolor-compatible environment rules.
/// </summary>
[TestClass]
public sealed class TerminalColorTests
{
    /// <summary>
    /// Verifies automatic color is disabled when stdout is not a terminal.
    /// </summary>
    [TestMethod]
    public void AutoColorRequiresTerminalOutput()
    {
        Assert.IsFalse(TerminalColor.ShouldEnableAutoColor(false, _ => "xterm-256color", isWindows: false));
        Assert.AreEqual(
            CliColorMode.Auto,
            TerminalColor.Resolve(CliColorMode.Auto, standardOutputIsTerminal: false, _ => "xterm-256color", isWindows: false));
    }

    /// <summary>
    /// Verifies Unix automatic color follows termcolor's TERM and NO_COLOR handling.
    /// </summary>
    [TestMethod]
    public void AutoColorMatchesTermcolorUnixEnvironmentRules()
    {
        Assert.IsFalse(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(), isWindows: false));
        Assert.IsFalse(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "dumb")), isWindows: false));
        Assert.IsFalse(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "xterm-256color"), ("NO_COLOR", string.Empty)), isWindows: false));
        Assert.IsTrue(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "xterm-256color")), isWindows: false));
    }

    /// <summary>
    /// Verifies Windows automatic color permits an absent TERM while honoring TERM=dumb and NO_COLOR.
    /// </summary>
    [TestMethod]
    public void AutoColorMatchesTermcolorWindowsEnvironmentRules()
    {
        Assert.IsTrue(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(), isWindows: true));
        Assert.IsFalse(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "dumb")), isWindows: true));
        Assert.IsFalse(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("NO_COLOR", string.Empty)), isWindows: true));
        Assert.IsTrue(TerminalColor.ShouldEnableAutoColor(true, UnixEnvironment(("TERM", "xterm-256color")), isWindows: true));
    }

    /// <summary>
    /// Verifies automatic color resolves to ANSI output only when terminal and environment checks allow color.
    /// </summary>
    [TestMethod]
    public void AutoColorResolvesToAnsiOnlyWhenEnvironmentAllowsIt()
    {
        Assert.AreEqual(
            CliColorMode.Ansi,
            TerminalColor.Resolve(CliColorMode.Auto, standardOutputIsTerminal: true, UnixEnvironment(("TERM", "xterm-256color")), isWindows: false));
        Assert.AreEqual(
            CliColorMode.Auto,
            TerminalColor.Resolve(CliColorMode.Auto, standardOutputIsTerminal: true, UnixEnvironment(("TERM", "dumb")), isWindows: false));
    }

    /// <summary>
    /// Verifies explicit color choices bypass automatic environment checks.
    /// </summary>
    [TestMethod]
    public void ExplicitColorModesDoNotConsultTheEnvironment()
    {
        Func<string, string?> throwingEnvironment = _ => throw new InvalidOperationException("environment should not be read");

        Assert.AreEqual(CliColorMode.Always, TerminalColor.Resolve(CliColorMode.Always, standardOutputIsTerminal: false, throwingEnvironment, isWindows: false));
        Assert.AreEqual(CliColorMode.Ansi, TerminalColor.Resolve(CliColorMode.Ansi, standardOutputIsTerminal: false, throwingEnvironment, isWindows: false));
        Assert.AreEqual(CliColorMode.Never, TerminalColor.Resolve(CliColorMode.Never, standardOutputIsTerminal: true, throwingEnvironment, isWindows: false));
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
