using System.Text;

namespace Scout.IO.Ignore;

internal sealed class FileTypePattern
{
    private readonly Glob glob;

    public FileTypePattern(FileTypeDefinition definition, string pattern, bool isNegated)
    {
        Definition = definition;
        IsNegated = isNegated;
        glob = Glob.Parse(Encoding.UTF8.GetBytes(pattern), GlobOptions.UnixLiteralSeparator);
    }

    public FileTypeDefinition Definition { get; }

    public bool IsNegated { get; }

    public bool IsMatch(byte[] fileName)
    {
        return glob.IsMatch(fileName);
    }
}
