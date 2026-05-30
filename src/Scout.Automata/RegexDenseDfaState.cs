namespace Scout;

internal sealed class RegexDenseDfaState
{
    public RegexDenseDfaState(int[] nfaStates, int acceptIndex, int[] transitions)
    {
        NfaStates = nfaStates;
        AcceptIndex = acceptIndex;
        Transitions = transitions;
    }

    public int[] NfaStates { get; }

    public int AcceptIndex { get; }

    public int[] Transitions { get; }
}
