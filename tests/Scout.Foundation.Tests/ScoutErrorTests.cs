
namespace Scout;

/// <summary>
/// Verifies Scout error cause-chain formatting.
/// </summary>
[TestClass]
public sealed class ScoutErrorTests
{
    /// <summary>
    /// Verifies default formatting renders only the top message.
    /// </summary>
    [TestMethod]
    public void FormatDefaultRendersTopMessage()
    {
        var error = new ScoutError("open failed", new ScoutError("permission denied"));

        Assert.AreEqual("open failed", error.FormatDefault());
        Assert.AreEqual("open failed", error.ToString());
    }

    /// <summary>
    /// Verifies alternate formatting joins the full cause chain with colon separators.
    /// </summary>
    [TestMethod]
    public void FormatAlternateRendersCauseChain()
    {
        ScoutError error = new ScoutError("search failed")
            .WithContext("while reading pattern file")
            .WithContext("scout");

        Assert.AreEqual("scout: while reading pattern file: search failed", error.FormatAlternate());
    }

    /// <summary>
    /// Verifies exceptions convert into Scout error chains.
    /// </summary>
    [TestMethod]
    public void FactoryConvertsExceptionChain()
    {
        InvalidOperationException exception = new("outer", new IOException("inner"));

        ScoutError error = ScoutErrorFactory.FromException(exception);

        Assert.AreEqual("outer: inner", error.FormatAlternate());
    }
}
