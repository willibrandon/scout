namespace Scout.Text.Regex;

/// <summary>
/// Handles a byte regex match while iterating through an input.
/// </summary>
/// <typeparam name="TState">The caller-owned state type.</typeparam>
/// <param name="input">The input being searched.</param>
/// <param name="match">The current match.</param>
/// <param name="state">Caller-owned state passed by reference.</param>
/// <returns><see langword="true" /> to continue iteration; otherwise, <see langword="false" />.</returns>
public delegate bool ByteRegexMatchHandler<TState>(ReadOnlySpan<byte> input, ByteRegexMatch match, ref TState state);
