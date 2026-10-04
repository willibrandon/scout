namespace Scout;

/// <summary>
/// Retains initial encoding resolution and decoder state between stream buffers.
/// </summary>
internal struct SearchTranscodingState
{
    internal bool ResolvedInitialEncoding;

    internal SearchEncodingKind EffectiveEncodingKind;

    internal Iso2022JpDecoderState Iso2022JpDecoderState;
}
