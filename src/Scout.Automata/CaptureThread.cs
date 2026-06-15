namespace Scout;

internal readonly struct CaptureThread
{
    public CaptureThread(int state, int position, int[] starts, int[] ends)
    {
        State = state;
        Position = position;
        Starts = starts;
        Ends = ends;
    }

    public int State { get; }

    public int Position { get; }

    public int[] Starts { get; }

    public int[] Ends { get; }

    public CaptureThread WithGroupEnd(int index, int end)
    {
        int[] ends = (int[])Ends.Clone();
        ends[index] = end;
        return new CaptureThread(State, Position, Starts, ends);
    }
}
