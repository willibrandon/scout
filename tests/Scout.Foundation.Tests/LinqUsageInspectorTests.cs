namespace Scout;

/// <summary>
/// Verifies the hot-path guard detects LINQ without confusing framework path and string APIs.
/// </summary>
[TestClass]
public sealed class LinqUsageInspectorTests
{
    /// <summary>
    /// Verifies overloaded framework names are checked by their resolved methods.
    /// </summary>
    /// <param name="expression">The expression to inspect.</param>
    /// <param name="expectedViolation">Whether the expression uses LINQ.</param>
    [TestMethod]
    [DataRow("Path.Join(\"root\", \"child\")", false)]
    [DataRow("string.Join(\",\", new[] { \"a\", \"b\" })", false)]
    [DataRow("new[] { 1 }.Join(new[] { 1 }, x => x, x => x, (x, y) => x)", true)]
    [DataRow("Enumerable.Where(new[] { 1 }, x => x > 0)", true)]
    [DataRow("from x in new[] { 1 } select x", true)]
    public void DistinguishesLinqFromOtherFrameworkMethods(string expression, bool expectedViolation)
    {
        string source = "using System; using System.IO; using System.Linq; class Example { object Run() => " + expression + "; }";
        Assert.AreEqual(expectedViolation, LinqUsageInspector.Find(source).Any());
    }
}
