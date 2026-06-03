
namespace Scout;

internal sealed class RegexLazyDfaState
{
    public RegexLazyDfaState(int[] nfaStates)
    {
        NfaStates = nfaStates;
    }

    public int[] NfaStates { get; }

    public Dictionary<byte, RegexLazyDfaState> Transitions { get; } = [];
}
