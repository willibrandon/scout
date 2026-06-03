
namespace Scout;

/// <summary>
/// Defines a named file type and the globs that recognize it.
/// </summary>
public sealed class FileTypeDefinition
{
    private readonly string[] globs;

    internal FileTypeDefinition(string name, string[] globs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(globs);

        Name = name;
        this.globs = globs;
    }

    /// <summary>
    /// Gets the file type name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the globs that recognize this file type.
    /// </summary>
    public IReadOnlyList<string> Globs => globs;
}
