namespace Scout;

internal struct Iso2022JpDecoderState
{
    public Iso2022JpState State;

    public Iso2022JpState OutputState;

    public byte Lead;

    public bool OutputFlag;
}
