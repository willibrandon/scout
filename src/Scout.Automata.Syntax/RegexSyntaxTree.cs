
namespace Scout;

/// <summary>
/// Represents a parsed regex pattern.
/// </summary>
public sealed class RegexSyntaxTree
{
    internal RegexSyntaxTree(ReadOnlyMemory<byte> pattern, RegexSyntaxNode root, int captureCount)
    {
        Pattern = pattern;
        Root = root;
        CaptureCount = captureCount;
    }

    /// <summary>
    /// Gets the original pattern bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Pattern { get; }

    /// <summary>
    /// Gets the root syntax node.
    /// </summary>
    public RegexSyntaxNode Root { get; }

    /// <summary>
    /// Gets the number of capturing groups in the pattern.
    /// </summary>
    public int CaptureCount { get; }
}
