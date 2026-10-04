namespace Scout;

/// <summary>
/// Verifies mutable resource scopes follow replacement and ownership transfer during unwinding.
/// </summary>
public sealed class DisposableScopeTests
{
    /// <summary>
    /// Verifies an exception disposes the current resource after the variable is replaced.
    /// </summary>
    [Fact]
    public void ExceptionDisposesReplacementInsteadOfInitialResource()
    {
        using var initial = new MemoryStream();
        using var replacement = new MemoryStream();
        Assert.Throws<IOException>(ReplaceAndFail);

        Assert.True(initial.CanRead);
        Assert.False(replacement.CanRead);

        void ReplaceAndFail()
        {
            MemoryStream resource = initial;
            using var scope = new DisposableScope<MemoryStream>(ref resource);
            resource = replacement;
            Assert.Same(replacement, resource);
            throw new IOException("The operation failed after replacing its resource.");
        }
    }

    /// <summary>
    /// Verifies a resource transferred out of a scope remains usable by its new owner.
    /// </summary>
    [Fact]
    public void OwnershipTransferLeavesResourceOpen()
    {
        using var transferred = new MemoryStream();
        MemoryStream? resource = transferred;
        using (new DisposableScope<MemoryStream?>(ref resource))
        {
            resource = null;
            Assert.Null(resource);
        }

        transferred.WriteByte(42);
        Assert.Equal([42], transferred.ToArray());
    }
}
