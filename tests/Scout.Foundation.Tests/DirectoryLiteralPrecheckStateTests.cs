namespace Scout;

/// <summary>
/// Verifies adaptive recursive literal precheck cutoff behavior.
/// </summary>
public sealed class DirectoryLiteralPrecheckStateTests
{
    /// <summary>
    /// Verifies the precheck is disabled after a high-hit sample.
    /// </summary>
    [Fact]
    public void DisablesAfterHighHitSample()
    {
        var state = new DirectoryLiteralPrecheckState();
        for (int index = 0; index < 16; index++)
        {
            state.Record(hit: true);
        }

        Assert.True(state.Enabled);
        for (int index = 0; index < 48; index++)
        {
            state.Record(hit: false);
        }

        Assert.False(state.Enabled);
    }

    /// <summary>
    /// Verifies miss-heavy samples keep the precheck active.
    /// </summary>
    [Fact]
    public void KeepsEnabledForMissHeavySample()
    {
        var state = new DirectoryLiteralPrecheckState();
        for (int index = 0; index < 64; index++)
        {
            state.Record(hit: false);
        }

        Assert.True(state.Enabled);
    }

    /// <summary>
    /// Verifies sparse later hits do not disable the precheck below the hit-rate threshold.
    /// </summary>
    [Fact]
    public void KeepsEnabledWhenSparseHitsEventuallyReachThreshold()
    {
        var state = new DirectoryLiteralPrecheckState();
        for (int index = 0; index < 15; index++)
        {
            state.Record(hit: true);
        }

        for (int index = 0; index < 84; index++)
        {
            state.Record(hit: false);
        }

        state.Record(hit: true);
        Assert.True(state.Enabled);
    }
}
