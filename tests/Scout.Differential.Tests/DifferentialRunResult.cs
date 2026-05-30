namespace Scout;

internal readonly struct DifferentialRunResult
{
    public DifferentialRunResult(int exitCode, byte[] output, string error)
    {
        ExitCode = exitCode;
        Output = output;
        Error = error;
    }

    public int ExitCode { get; }

    public byte[] Output { get; }

    public string Error { get; }
}
