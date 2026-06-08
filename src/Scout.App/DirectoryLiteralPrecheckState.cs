namespace Scout;

internal sealed class DirectoryLiteralPrecheckState
{
    private const int SampleSize = 64;
    private const int DisableHitThreshold = SampleSize / 4;

    private int attempts;
    private int hits;
    private int disabled;

    public bool Enabled => Volatile.Read(ref disabled) == 0;

    public void Record(bool hit)
    {
        int currentHits = hit ? Interlocked.Increment(ref hits) : Volatile.Read(ref hits);
        int currentAttempts = Interlocked.Increment(ref attempts);
        if (currentAttempts >= SampleSize &&
            currentHits >= DisableHitThreshold &&
            currentHits * 4 >= currentAttempts)
        {
            Interlocked.Exchange(ref disabled, 1);
        }
    }
}
