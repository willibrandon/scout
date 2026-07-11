namespace Scout;

/// <summary>
/// Defines the xUnit collection that isolates the large bounded Unicode class throughput regression test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LargeBoundedUnicodeClassTestGroup
{
    /// <summary>
    /// Names the xUnit collection that isolates the large bounded Unicode class throughput regression test.
    /// </summary>
    public const string Name = "large bounded Unicode class";
}
