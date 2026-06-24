namespace Scout;

internal static class RegexSpecializationModeDefaults
{
    private static int current;

    public static RegexSpecializationMode Current
    {
        get => (RegexSpecializationMode)System.Threading.Volatile.Read(ref current);
        set => System.Threading.Volatile.Write(ref current, (int)value);
    }

    public static RegexSpecializationModeScope Use(RegexSpecializationMode mode)
    {
        RegexSpecializationMode previous = Current;
        Current = mode;
        return new RegexSpecializationModeScope(previous);
    }
}
