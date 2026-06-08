
namespace Scout;

internal sealed class RegexLazyDfaState
{
    public RegexLazyDfaState(int[] nfaStates, int acceptIndex)
    {
        NfaStates = nfaStates;
        AcceptIndex = acceptIndex;
    }

    public int[] NfaStates { get; }

    public int AcceptIndex { get; }

    public Dictionary<byte, RegexLazyDfaState> Transitions { get; } = [];
}
