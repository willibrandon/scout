namespace Scout;

/// <summary>
/// Identifies a binary character-class set operation.
/// </summary>
internal enum RegexClassSetBinaryOperator
{
    /// <summary>
    /// Retains scalars present in both operands.
    /// </summary>
    Intersection,

    /// <summary>
    /// Removes scalars present in the right operand from the left operand.
    /// </summary>
    Difference,

    /// <summary>
    /// Retains scalars present in exactly one operand.
    /// </summary>
    SymmetricDifference,
}
