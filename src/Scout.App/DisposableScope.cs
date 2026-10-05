namespace Scout;

/// <summary>
/// Disposes the current value of a resource that must remain mutable within its scope.
/// </summary>
/// <typeparam name="T">The disposable resource type.</typeparam>
/// <param name="resource">The resource, including any replacement or ownership transfer.</param>
internal readonly ref struct DisposableScope<T>(ref T resource) : IDisposable
    where T : IDisposable?
{
    private readonly ref T _resource = ref resource;

    /// <summary>
    /// Disposes the resource still owned by the scope.
    /// </summary>
    public void Dispose()
    {
        _resource?.Dispose();
    }
}
