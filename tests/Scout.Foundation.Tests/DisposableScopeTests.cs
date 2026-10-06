namespace Scout;

/// <summary>
/// Verifies mutable resource scopes follow replacement and ownership transfer during unwinding.
/// </summary>
[TestClass]
public sealed class DisposableScopeTests
{
    /// <summary>
    /// Verifies an exception disposes the current resource after the variable is replaced.
    /// </summary>
    [TestMethod]
    public void ExceptionDisposesReplacementInsteadOfInitialResource()
    {
        using var initial = new MemoryStream();
        using var replacement = new MemoryStream();
        Assert.ThrowsExactly<IOException>(ReplaceAndFail);

        Assert.IsTrue(initial.CanRead);
        Assert.IsFalse(replacement.CanRead);

        void ReplaceAndFail()
        {
            MemoryStream resource = initial;
            using var scope = new DisposableScope<MemoryStream>(ref resource);
            resource = replacement;
            Assert.AreSame(replacement, resource);
            throw new IOException("The operation failed after replacing its resource.");
        }
    }

    /// <summary>
    /// Verifies a resource transferred out of a scope remains usable by its new owner.
    /// </summary>
    [TestMethod]
    public void OwnershipTransferLeavesResourceOpen()
    {
        using var transferred = new MemoryStream();
        MemoryStream? resource = transferred;
        using (new DisposableScope<MemoryStream?>(ref resource))
        {
            resource = null;
            Assert.IsNull(resource);
        }

        transferred.WriteByte(42);
        Assert.AreSequenceEqual<byte>([42], transferred.ToArray());
    }
}
