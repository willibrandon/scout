namespace Scout;

/// <summary>
/// Verifies reference-resource ownership on exceptional construction and successful transfer.
/// </summary>
public sealed class DisposableOwnerTests
{
    /// <summary>
    /// Verifies failure after acquiring a resource releases it before leaving the operation.
    /// </summary>
    [Fact]
    public void FailureAfterAcquisitionDisposesResource()
    {
        using var stream = new MemoryStream();
        Assert.Throws<IOException>(AcquireAndFail);

        Assert.False(stream.CanRead);

        void AcquireAndFail()
        {
            using var owner = new DisposableOwner<MemoryStream> { Resource = stream };
            throw new IOException("The next construction step failed.");
        }
    }

    /// <summary>
    /// Verifies releasing ownership prevents cleanup from closing a transferred resource.
    /// </summary>
    [Fact]
    public void ReleasedResourceRemainsUsable()
    {
        using var stream = new MemoryStream();
        using (var owner = new DisposableOwner<MemoryStream> { Resource = stream })
        {
            owner.Release();
        }

        stream.WriteByte(42);
        Assert.Equal([42], stream.ToArray());
    }
}
