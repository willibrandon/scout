namespace Scout;

/// <summary>
/// Defines the xUnit collection that protects process-wide app state from parallel tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApplicationProcessStateGroup
{
    /// <summary>
    /// Names the xUnit collection that serializes in-process application tests.
    /// </summary>
    public const string Name = "application process state";
}
