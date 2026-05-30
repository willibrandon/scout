namespace Scout;

/// <summary>
/// Represents a capturing or non-capturing regex group.
/// </summary>
public sealed class RegexGroupNode : RegexSyntaxNode
{
    internal RegexGroupNode(
        RegexSyntaxKind kind,
        RegexSyntaxNode child,
        int captureIndex,
        string? captureName,
        string enabledFlags,
        string disabledFlags,
        int position)
        : base(kind, position)
    {
        Child = child;
        CaptureIndex = captureIndex;
        CaptureName = captureName;
        EnabledFlags = enabledFlags;
        DisabledFlags = disabledFlags;
    }

    /// <summary>
    /// Gets the grouped child expression.
    /// </summary>
    public RegexSyntaxNode Child { get; }

    /// <summary>
    /// Gets the one-based capture index, or zero for non-capturing groups.
    /// </summary>
    public int CaptureIndex { get; }

    /// <summary>
    /// Gets the capture name, when the group is named.
    /// </summary>
    public string? CaptureName { get; }

    /// <summary>
    /// Gets inline flags enabled for this group.
    /// </summary>
    public string EnabledFlags { get; }

    /// <summary>
    /// Gets inline flags disabled for this group.
    /// </summary>
    public string DisabledFlags { get; }
}
