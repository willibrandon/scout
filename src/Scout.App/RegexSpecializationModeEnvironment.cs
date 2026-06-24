namespace Scout;

internal static class RegexSpecializationModeEnvironment
{
    internal const string VariableName = "SCOUT_REGEX_SPECIALIZATION_MODE";

    public static bool TryResolve(out RegexSpecializationMode mode, out ScoutError? error)
    {
        mode = RegexSpecializationMode.Default;
        error = null;
        string? value = ProcessEnvironment.GetVariable(VariableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase))
        {
            mode = RegexSpecializationMode.Default;
            return true;
        }

        if (string.Equals(value, "general", StringComparison.OrdinalIgnoreCase))
        {
            mode = RegexSpecializationMode.General;
            return true;
        }

        if (string.Equals(value, "fallback", StringComparison.OrdinalIgnoreCase))
        {
            mode = RegexSpecializationMode.Fallback;
            return true;
        }

        error = new ScoutError($"{VariableName} must be one of: default, general, fallback");
        return false;
    }
}
