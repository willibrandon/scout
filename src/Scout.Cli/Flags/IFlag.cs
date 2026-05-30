namespace Scout.Flags;

/// <summary>
/// Defines one command-line flag for the generated Scout flag catalog.
/// </summary>
/// <typeparam name="TSelf">The concrete flag definition type.</typeparam>
internal interface IFlag<TSelf>
    where TSelf : IFlag<TSelf>
{
    /// <summary>
    /// Gets the generated-catalog descriptor for this flag.
    /// </summary>
    static abstract FlagDescriptor Descriptor { get; }
}
