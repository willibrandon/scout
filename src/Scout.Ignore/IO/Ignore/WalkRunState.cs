namespace Scout.IO.Ignore;

/// <summary>
/// Owns cancellation and error reporting for a single traversal operation.
/// </summary>
internal sealed class WalkRunState(Func<WalkException, WalkState>? errorHandler)
{
    private int quit;

    internal bool IsQuitRequested => Volatile.Read(ref quit) != 0;

    internal bool ThrowsErrors => errorHandler is null;

    internal void ReportError(WalkException error)
    {
        if (errorHandler is null)
        {
            throw error;
        }

        if (errorHandler(error) == WalkState.Quit)
        {
            Volatile.Write(ref quit, 1);
        }
    }

    internal void ReportErrors(ReadOnlySpan<WalkException> errors)
    {
        foreach (WalkException error in errors)
        {
            if (IsQuitRequested)
            {
                return;
            }

            ReportError(error);
        }
    }
}
