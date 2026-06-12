namespace Scout;

internal readonly struct RegexNfaSparseCacheKey : IEquatable<RegexNfaSparseCacheKey>
{
    private readonly RegexNfaSparseTransition[] transitions;
    private readonly int hashCode;

    public RegexNfaSparseCacheKey(RegexNfaSparseTransition[] transitions)
    {
        this.transitions = transitions;
        hashCode = ComputeHashCode(transitions);
    }

    public bool Equals(RegexNfaSparseCacheKey other)
    {
        if (ReferenceEquals(transitions, other.transitions))
        {
            return true;
        }

        if (transitions is null || other.transitions is null || transitions.Length != other.transitions.Length)
        {
            return false;
        }

        return transitions.AsSpan().SequenceEqual(other.transitions);
    }

    public override bool Equals(object? obj)
    {
        return obj is RegexNfaSparseCacheKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return hashCode;
    }

    private static int ComputeHashCode(ReadOnlySpan<RegexNfaSparseTransition> transitions)
    {
        unchecked
        {
            int hash = 17;
            for (int index = 0; index < transitions.Length; index++)
            {
                RegexNfaSparseTransition transition = transitions[index];
                hash = hash * 31 + transition.Start;
                hash = hash * 31 + transition.End;
                hash = hash * 31 + transition.Next;
            }

            return hash;
        }
    }
}
