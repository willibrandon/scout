
namespace Scout;

internal sealed class RegexLazyDfaState
{
    private const int DenseTransitionThreshold = 4;

    private Dictionary<byte, RegexLazyDfaState>? sparseTransitions = [];
    private RegexLazyDfaState?[]? denseTransitions;
    private bool acceleratorComputed;
    private byte[]? acceleratorNeedles;

    public RegexLazyDfaState(int[] nfaStates, int acceptIndex)
    {
        NfaStates = nfaStates;
        AcceptIndex = acceptIndex;
    }

    public int[] NfaStates { get; }

    public int AcceptIndex { get; }

    public bool TryGetAccelerator(out byte[] needles)
    {
        needles = acceleratorNeedles ?? [];
        return acceleratorComputed && acceleratorNeedles is not null;
    }

    public void SetAccelerator(byte[]? needles)
    {
        acceleratorNeedles = needles;
        acceleratorComputed = true;
    }

    public bool AcceleratorComputed => acceleratorComputed;

    public bool TryGetTransition(byte value, out RegexLazyDfaState? state)
    {
        RegexLazyDfaState?[]? dense = denseTransitions;
        if (dense is not null)
        {
            state = dense[value];
            return state is not null;
        }

        return sparseTransitions!.TryGetValue(value, out state);
    }

    public void AddTransition(byte value, RegexLazyDfaState state)
    {
        RegexLazyDfaState?[]? dense = denseTransitions;
        if (dense is not null)
        {
            dense[value] = state;
            return;
        }

        Dictionary<byte, RegexLazyDfaState> sparse = sparseTransitions!;
        sparse.Add(value, state);
        if (sparse.Count < DenseTransitionThreshold)
        {
            return;
        }

        dense = new RegexLazyDfaState[256];
        foreach (KeyValuePair<byte, RegexLazyDfaState> transition in sparse)
        {
            dense[transition.Key] = transition.Value;
        }

        denseTransitions = dense;
        sparseTransitions = null;
    }
}
