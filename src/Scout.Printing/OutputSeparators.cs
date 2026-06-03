
namespace Scout;

internal readonly struct OutputSeparators
{
    public OutputSeparators(
        ReadOnlyMemory<byte> fieldMatch,
        ReadOnlyMemory<byte> fieldContext,
        ReadOnlyMemory<byte> context,
        bool contextEnabled,
        ReadOnlyMemory<byte> lineTerminator)
    {
        FieldMatch = fieldMatch;
        FieldContext = fieldContext;
        Context = context;
        ContextEnabled = contextEnabled;
        LineTerminator = lineTerminator;
    }

    public ReadOnlyMemory<byte> FieldMatch { get; }

    public ReadOnlyMemory<byte> FieldContext { get; }

    public ReadOnlyMemory<byte> Context { get; }

    public bool ContextEnabled { get; }

    public ReadOnlyMemory<byte> LineTerminator { get; }

    public bool Crlf => LineTerminator.Length == 2 && LineTerminator.Span[0] == (byte)'\r' && LineTerminator.Span[1] == (byte)'\n';

    public bool NullData => LineTerminator.Length == 1 && LineTerminator.Span[0] == 0;
}
