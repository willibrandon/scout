using System.Collections.ObjectModel;

namespace Scout;

/// <summary>
/// Describes an external decompression command associated with a path glob.
/// </summary>
public sealed class CliDecompressionCommand
{
    private readonly string[] arguments;

    /// <summary>
    /// Initializes a new instance of the <see cref="CliDecompressionCommand" /> class.
    /// </summary>
    /// <param name="glob">The glob associated with this decompression command.</param>
    /// <param name="program">The external decompression program.</param>
    /// <param name="arguments">The fixed program arguments before the file path is appended.</param>
    public CliDecompressionCommand(string glob, string program, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(glob);
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(arguments);

        Glob = glob;
        Program = program;
        this.arguments = (string[])arguments.Clone();
        Arguments = Array.AsReadOnly(this.arguments);
    }

    /// <summary>
    /// Gets the glob associated with this decompression command.
    /// </summary>
    public string Glob { get; }

    /// <summary>
    /// Gets the external decompression program.
    /// </summary>
    public string Program { get; }

    /// <summary>
    /// Gets the fixed program arguments before the file path is appended.
    /// </summary>
    public ReadOnlyCollection<string> Arguments { get; }

    /// <summary>
    /// Creates a command-line argument list by appending the file path.
    /// </summary>
    /// <param name="path">The path to append after the fixed decompression arguments.</param>
    /// <returns>A new argument array for invoking the decompression command.</returns>
    public string[] CreateArguments(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] commandArguments = new string[arguments.Length + 1];
        arguments.CopyTo(commandArguments, 0);
        commandArguments[^1] = path;
        return commandArguments;
    }

    internal CliDecompressionCommand WithProgram(string program)
    {
        return new CliDecompressionCommand(Glob, program, arguments);
    }
}
