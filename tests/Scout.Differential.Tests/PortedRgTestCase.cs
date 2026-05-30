using System;

namespace Scout;

internal sealed class PortedRgTestCase
{
    public PortedRgTestCase(string sourceFile, string name, Action<RgTestDirectory> arrange, DifferentialCase command)
    {
        SourceFile = sourceFile;
        Name = name;
        Arrange = arrange;
        Command = command;
    }

    public string SourceFile { get; }

    public string Name { get; }

    public Action<RgTestDirectory> Arrange { get; }

    public DifferentialCase Command { get; }
}
