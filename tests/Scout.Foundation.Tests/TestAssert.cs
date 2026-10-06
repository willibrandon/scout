namespace Scout;

/// <summary>
/// Applies assertions to individual collection elements with their positions in failure messages.
/// </summary>
internal static class TestAssert
{
    /// <summary>
    /// Verifies each element satisfies an assertion.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The elements to inspect.</param>
    /// <param name="assertion">The assertion to apply.</param>
    internal static void All<T>(IEnumerable<T> values, Action<T> assertion)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(assertion);
        int index = 0;
        foreach (T value in values)
        {
            Verify(value, assertion, index++);
        }
    }

    /// <summary>
    /// Verifies a collection has one element for each assertion and applies them in order.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The elements to inspect.</param>
    /// <param name="assertions">The ordered element assertions.</param>
    internal static void Collection<T>(IEnumerable<T> values, params Action<T>[] assertions)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(assertions);
        T[] items = values.ToArray();
        Assert.HasCount(assertions.Length, items);
        for (int index = 0; index < items.Length; index++)
        {
            Verify(items[index], assertions[index], index);
        }
    }

    private static void Verify<T>(T value, Action<T> assertion, int index)
    {
        AssertFailedException? failure = null;
        try
        {
            assertion(value);
        }
        catch (AssertFailedException exception)
        {
            failure = exception;
        }

        if (failure is not null)
        {
            throw new AssertFailedException($"Element {index}: {failure.Message}", failure);
        }
    }
}
