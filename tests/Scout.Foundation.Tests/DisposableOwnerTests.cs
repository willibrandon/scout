namespace Scout;

/// <summary>
/// Verifies reference-resource ownership on exceptional construction and successful transfer.
/// </summary>
[TestClass]
public sealed class DisposableOwnerTests
{
    /// <summary>
    /// Verifies failure after acquiring a resource releases it before leaving the operation.
    /// </summary>
    [TestMethod]
    public void FailureAfterAcquisitionDisposesResource()
    {
        using var stream = new MemoryStream();
        Assert.ThrowsExactly<IOException>(AcquireAndFail);

        Assert.IsFalse(stream.CanRead);

        void AcquireAndFail()
        {
            using var owner = new DisposableOwner<MemoryStream> { Resource = stream };
            throw new IOException("The next construction step failed.");
        }
    }

    /// <summary>
    /// Verifies releasing ownership prevents cleanup from closing a transferred resource.
    /// </summary>
    [TestMethod]
    public void ReleasedResourceRemainsUsable()
    {
        using var stream = new MemoryStream();
        using (var owner = new DisposableOwner<MemoryStream> { Resource = stream })
        {
            owner.Release();
        }

        stream.WriteByte(42);
        Assert.AreSequenceEqual<byte>([42], stream.ToArray());
    }
}
