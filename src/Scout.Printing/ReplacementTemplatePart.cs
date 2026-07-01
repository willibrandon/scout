namespace Scout;

internal readonly record struct ReplacementTemplatePart(
    byte[]? Literal,
    int CaptureIndex,
    string? CaptureName)
{
    public bool IsLiteral => Literal is not null;
}
