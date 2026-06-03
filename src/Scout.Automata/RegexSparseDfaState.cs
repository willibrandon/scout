
namespace Scout;

internal sealed class RegexSparseDfaState
{
    public RegexSparseDfaState(int[] nfaStates, int acceptIndex, Dictionary<byte, int> transitions)
    {
        NfaStates = nfaStates;
        AcceptIndex = acceptIndex;
        Transitions = transitions;
    }

    public int[] NfaStates { get; }

    public int AcceptIndex { get; }

    public Dictionary<byte, int> Transitions { get; }
}
