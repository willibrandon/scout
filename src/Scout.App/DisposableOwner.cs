namespace Scout;

/// <summary>
/// Owns a disposable reference until it is released to a longer-lived owner.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class DisposableOwner<T> : IDisposable
    where T : class, IDisposable
{
    /// <summary>
    /// The resource whose construction and lifetime belong to this owner.
    /// </summary>
    internal T? Resource;

    /// <summary>
    /// Relinquishes responsibility for a resource transferred to another owner.
    /// </summary>
    internal void Release()
    {
        Resource = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Resource?.Dispose();
        Resource = null;
    }
}
