namespace Scout;

/// <summary>
/// Describes the outcome of an attempted capture-aware one-pass replay.
/// </summary>
internal enum RegexCaptureOnePassResult
{
    /// <summary>
    /// The exact match was replayed and all capture slots were populated.
    /// </summary>
    Success,

    /// <summary>
    /// The replay requires the ordered NFA fallback.
    /// </summary>
    Fallback,
}
