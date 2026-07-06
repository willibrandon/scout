namespace Scout;

internal static class RegexSpecializationModeDefaults
{
    private static readonly AsyncLocal<RegexSpecializationMode?> CurrentMode = new();

    public static RegexSpecializationMode Current
    {
        get => CurrentMode.Value ?? RegexSpecializationMode.Default;
        set => CurrentMode.Value = value;
    }

    public static RegexSpecializationModeScope Use(RegexSpecializationMode mode)
    {
        RegexSpecializationMode previous = Current;
        Current = mode;
        return new RegexSpecializationModeScope(previous);
    }
}
