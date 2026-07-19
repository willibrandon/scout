namespace Scout;

/// <summary>
/// Represents a parsed bracketed character class and its outer negation.
/// </summary>
internal sealed class RegexCharacterClass(bool negated, RegexClassSetNode expression)
{
    internal bool Negated { get; } = negated;

    internal RegexClassSetNode Expression { get; } = expression;
}
