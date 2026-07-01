namespace Scout;

internal readonly struct RegexSpecializationModeScope : IDisposable
{
    private readonly RegexSpecializationMode previous;

    public RegexSpecializationModeScope(RegexSpecializationMode previous)
    {
        this.previous = previous;
    }

    public void Dispose()
    {
        RegexSpecializationModeDefaults.Current = previous;
    }
}
