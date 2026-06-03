
namespace Scout;

internal readonly struct RegexDfaStateKey : IEquatable<RegexDfaStateKey>
{
    private readonly int[] states;

    public RegexDfaStateKey(int[] states)
    {
        this.states = states;
    }

    public bool Equals(RegexDfaStateKey other)
    {
        return states.AsSpan().SequenceEqual(other.states);
    }

    public override bool Equals(object? obj)
    {
        return obj is RegexDfaStateKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (int index = 0; index < states.Length; index++)
        {
            hash.Add(states[index]);
        }

        return hash.ToHashCode();
    }
}
