namespace Scout;

/// <summary>
/// Identifies an ordered delayed-match DFA state together with its preceding byte context.
/// </summary>
/// <param name="states">The ordered NFA state roots pending contextual closure.</param>
/// <param name="previousContext">The equivalence class of the byte before the next input unit.</param>
/// <param name="accepting">Whether the preceding boundary accepted a match.</param>
internal readonly struct RegexLookaroundDfaStateKey(
    int[] states,
    byte previousContext,
    bool accepting) : IEquatable<RegexLookaroundDfaStateKey>
{
    private readonly int[] _states = states;
    private readonly byte _previousContext = previousContext;
    private readonly bool _accepting = accepting;

    /// <summary>
    /// Determines whether this key identifies the same ordered NFA roots and boundary context.
    /// </summary>
    /// <param name="other">The key to compare.</param>
    /// <returns><see langword="true" /> when the keys are equal.</returns>
    public bool Equals(RegexLookaroundDfaStateKey other)
    {
        return _previousContext == other._previousContext &&
            _accepting == other._accepting &&
            _states.AsSpan().SequenceEqual(other._states);
    }

    /// <summary>
    /// Determines whether an object identifies the same DFA state.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns><see langword="true" /> when the object is an equal state key.</returns>
    public override bool Equals(object? obj)
    {
        return obj is RegexLookaroundDfaStateKey other && Equals(other);
    }

    /// <summary>
    /// Computes a hash code for the ordered NFA roots and boundary context.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_previousContext);
        hash.Add(_accepting);
        for (int index = 0; index < _states.Length; index++)
        {
            hash.Add(_states[index]);
        }

        return hash.ToHashCode();
    }
}
