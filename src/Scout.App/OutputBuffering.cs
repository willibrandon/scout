namespace Scout;

internal static class OutputBuffering
{
    internal static RawByteWriterBufferMode Resolve(CliBufferMode bufferMode, bool standardOutputIsTerminal)
    {
        return bufferMode switch
        {
            CliBufferMode.Auto => standardOutputIsTerminal ? RawByteWriterBufferMode.Line : RawByteWriterBufferMode.Block,
            CliBufferMode.Line => RawByteWriterBufferMode.Line,
            CliBufferMode.Block => RawByteWriterBufferMode.Block,
            _ => RawByteWriterBufferMode.Block,
        };
    }
}
