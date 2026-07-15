namespace Scout;

/// <summary>
/// Defines the xUnit collection that isolates the mixed-alternation throughput regression test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MixedAlternationThroughputTestGroup()
{
    /// <summary>
    /// Names the xUnit collection that isolates the mixed-alternation throughput regression test.
    /// </summary>
    public const string Name = "mixed alternation throughput";
}
