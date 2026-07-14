namespace Scout;

/// <summary>
/// Defines the xUnit collection that isolates the bounded URL capture throughput regression test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BoundedUrlCaptureTestGroup()
{
    /// <summary>
    /// Names the xUnit collection that isolates the bounded URL capture throughput regression test.
    /// </summary>
    public const string Name = "bounded URL capture throughput";
}
