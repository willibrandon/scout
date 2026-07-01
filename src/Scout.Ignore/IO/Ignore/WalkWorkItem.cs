
namespace Scout.IO.Ignore;

internal sealed class WalkWorkItem
{
    public WalkWorkItem(
        WalkPath path,
        int depth,
        HashSet<FileIdentity> ancestors,
        IgnoreStack ignoreStack,
        FileSystemDevice rootDevice,
        bool isRoot)
    {
        Path = path;
        Depth = depth;
        Ancestors = ancestors;
        IgnoreStack = ignoreStack;
        RootDevice = rootDevice;
        IsRoot = isRoot;
    }

    public WalkPath Path { get; }

    public int Depth { get; }

    public HashSet<FileIdentity> Ancestors { get; }

    public IgnoreStack IgnoreStack { get; }

    public FileSystemDevice RootDevice { get; }

    public bool IsRoot { get; }
}
