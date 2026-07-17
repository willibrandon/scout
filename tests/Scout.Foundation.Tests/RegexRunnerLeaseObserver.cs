namespace Scout;

/// <summary>
/// Observes nested Pike VM leases while a public match iterator owns its operation-scoped runner.
/// </summary>
/// <param name="automaton">The automaton used to rent nested runners.</param>
internal sealed class RegexRunnerLeaseObserver(RegexAutomaton automaton)
{
    private readonly RegexAutomaton _automaton = automaton;

    /// <summary>
    /// Gets the first nested Pike VM lease generation.
    /// </summary>
    public long FirstLeaseVersion { get; private set; }

    /// <summary>
    /// Gets the number of nested runner leases observed.
    /// </summary>
    public int ObservationCount { get; private set; }

    /// <summary>
    /// Rents one nested runner and records its Pike VM lease generation.
    /// </summary>
    public void Observe()
    {
        using RegexFindRunner runner = _automaton.RentFindRunner();
        if (ObservationCount == 0)
        {
            FirstLeaseVersion = runner.PikeVmLeaseVersion;
        }

        ObservationCount++;
    }
}
