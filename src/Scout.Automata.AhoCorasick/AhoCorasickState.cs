using System.Collections.Generic;

namespace Scout;

internal sealed class AhoCorasickState
{
    public Dictionary<byte, int> Transitions { get; } = [];

    public List<AhoCorasickOutput> Outputs { get; } = [];

    public int Failure { get; set; }
}
