namespace Scout;

/// <summary>
/// Carries output and exit-status state through one search operation.
/// </summary>
internal struct SearchExecutionState
{
    internal bool WroteHeadingOutput;
    internal bool Matched;
    internal bool Errored;
}
