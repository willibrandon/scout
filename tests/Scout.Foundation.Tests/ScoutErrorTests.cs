using System;
using System.IO;

namespace Scout;

/// <summary>
/// Verifies Scout error cause-chain formatting.
/// </summary>
public sealed class ScoutErrorTests
{
    /// <summary>
    /// Verifies default formatting renders only the top message.
    /// </summary>
    [Fact]
    public void FormatDefaultRendersTopMessage()
    {
        var error = new ScoutError("open failed", new ScoutError("permission denied"));

        Assert.Equal("open failed", error.FormatDefault());
        Assert.Equal("open failed", error.ToString());
    }

    /// <summary>
    /// Verifies alternate formatting joins the full cause chain with colon separators.
    /// </summary>
    [Fact]
    public void FormatAlternateRendersCauseChain()
    {
        ScoutError error = new ScoutError("search failed")
            .WithContext("while reading pattern file")
            .WithContext("scout");

        Assert.Equal("scout: while reading pattern file: search failed", error.FormatAlternate());
    }

    /// <summary>
    /// Verifies exceptions convert into Scout error chains.
    /// </summary>
    [Fact]
    public void FactoryConvertsExceptionChain()
    {
        InvalidOperationException exception = new("outer", new IOException("inner"));

        ScoutError error = ScoutErrorFactory.FromException(exception);

        Assert.Equal("outer: inner", error.FormatAlternate());
    }
}
