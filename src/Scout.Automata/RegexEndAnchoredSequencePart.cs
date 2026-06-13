namespace Scout;

internal readonly record struct RegexEndAnchoredSequencePart(
    RegexSimpleSequenceSegment Segment,
    int Minimum,
    int? Maximum);
