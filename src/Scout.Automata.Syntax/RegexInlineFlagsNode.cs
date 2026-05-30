namespace Scout;

/// <summary>
/// Represents an inline flag change such as <c>(?i)</c>.
/// </summary>
public sealed class RegexInlineFlagsNode : RegexSyntaxNode
{
    internal RegexInlineFlagsNode(string enabledFlags, string disabledFlags, int position)
        : base(RegexSyntaxKind.InlineFlags, position)
    {
        EnabledFlags = enabledFlags;
        DisabledFlags = disabledFlags;
    }

    /// <summary>
    /// Gets inline flags enabled at this position.
    /// </summary>
    public string EnabledFlags { get; }

    /// <summary>
    /// Gets inline flags disabled at this position.
    /// </summary>
    public string DisabledFlags { get; }
}
