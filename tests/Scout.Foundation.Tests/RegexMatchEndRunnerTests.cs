namespace Scout;

/// <summary>
/// Verifies operation-scoped match-end runner ownership.
/// </summary>
[TestClass]
public sealed class RegexMatchEndRunnerTests
{
    /// <summary>
    /// Verifies copied values cannot use or return the same mutable DFA after one copy ends the
    /// shared generation lease.
    /// </summary>
    [TestMethod]
    public void CopiedValueCannotUseOrReturnDfaTwice()
    {
        RegexUnanchoredLazyDfa CreateRunner()
        {
            RegexSyntaxTree tree = RegexSyntaxParser.Parse("ab"u8);
            var options = new RegexCompileOptions(
                caseInsensitive: false,
                swapGreed: false,
                multiLine: true,
                dotMatchesNewline: false,
                utf8: false,
                unicodeClasses: false,
                excludeLineTerminators: true);
            Assert.IsTrue(RegexUnanchoredLazyDfa.TryCreate(
                tree.Root,
                options,
                dfaSizeLimit: 1024 * 1024,
                out RegexUnanchoredLazyDfa? runner));
            return runner!;
        }

        RegexUnanchoredLazyDfa initial = CreateRunner();
        var pool = new RegexRunnerPool<RegexUnanchoredLazyDfa>(initial, CreateRunner);
        RegexUnanchoredLazyDfa rented = Assert.IsExactInstanceOfType<RegexUnanchoredLazyDfa>(pool.Rent());
        var runner = new RegexMatchEndRunner(
            pool,
            rented,
            rented.BeginRunnerLease(),
            denseDfa: null,
            usesAsciiProjection: false);
        RegexMatchEndRunner copy = runner;
        Assert.IsTrue(runner.SharesPooledStateWith(copy));

        runner.Dispose();

        Assert.IsFalse(copy.IsAvailable);
        ObjectDisposedException? exception = null;
        try
        {
            _ = copy.TryFindEnd("!!ab!!"u8, startAt: 0, out _, out _);
        }
        catch (ObjectDisposedException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception);
        copy.Dispose();

        RegexUnanchoredLazyDfa first = Assert.IsExactInstanceOfType<RegexUnanchoredLazyDfa>(pool.Rent());
        RegexUnanchoredLazyDfa second = Assert.IsExactInstanceOfType<RegexUnanchoredLazyDfa>(pool.Rent());
        try
        {
            Assert.AreNotSame(first, second);
        }
        finally
        {
            pool.Return(first);
            pool.Return(second);
        }
    }
}
