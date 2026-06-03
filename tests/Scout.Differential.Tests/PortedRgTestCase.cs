
namespace Scout;

internal sealed class PortedRgTestCase
{
    public PortedRgTestCase(string sourceFile, string name, Action<RgTestDirectory> arrange, params DifferentialCase[] commands)
    {
        if (commands.Length == 0)
        {
            throw new ArgumentException("At least one command is required.", nameof(commands));
        }

        SourceFile = sourceFile;
        Name = name;
        Arrange = arrange;
        Commands = commands;
    }

    public string SourceFile { get; }

    public string Name { get; }

    public Action<RgTestDirectory> Arrange { get; }

    public DifferentialCase[] Commands { get; }
}
