
namespace Scout;

internal readonly struct OutputLineLimit
{
    public OutputLineLimit(ulong? maxColumns, bool preview)
    {
        MaxColumns = maxColumns;
        Preview = preview;
    }

    public ulong? MaxColumns { get; }

    public bool Preview { get; }

    public bool IsEnabled => MaxColumns is ulong columns && columns > 0;

    public bool IsExceeded(ReadOnlySpan<byte> line)
    {
        return MaxColumns is ulong columns && columns > 0 && (ulong)line.Length > columns;
    }

    public int GetPreviewLength(ReadOnlySpan<byte> line)
    {
        if (MaxColumns is not ulong columns || columns == 0 || columns >= (ulong)line.Length)
        {
            return line.Length;
        }

        return (int)columns;
    }
}
