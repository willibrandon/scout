using Scout.IO.Globbing;
using Scout.IO.Ignore;
using Scout.Text.Regex;

internal static class Program
{
    private static void Main()
    {
        foreach (ByteRegexEngineMode mode in Enum.GetValues<ByteRegexEngineMode>())
        {
            var options = new ByteRegexOptions { EngineMode = mode };
            ByteRegex comparison = ByteRegex.Compile(@"[\P{gc!=Separator}]", options);
            Require(comparison.IsMatch(" "u8) && !comparison.IsMatch("A"u8), "Unicode comparison negation");
            ByteRegex capture = ByteRegex.Compile("(abc)(ABC){0}", options);
            ByteRegexCaptures captures = capture.FindCaptures("abcABC"u8) ?? throw new InvalidOperationException("capture match");
            Require(captures.GroupCount == 3 && captures.GetGroup(2) is null && captures.Match.Length == 3, "capture slots");
        }

        Require(Glob.Parse("*.mojo"u8.ToArray()).IsMatch("main.mojo"u8), "glob matching");
        Require(!Walk.FromPaths(Array.Empty<string>()).Any(), "empty walk");
        string root = Path.Combine(Path.GetTempPath(), "scout-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a"));
            Directory.CreateDirectory(Path.Combine(root, "b"));
            File.WriteAllText(Path.Combine(root, ".rgignore"), "a/ignored\n");
            File.WriteAllText(Path.Combine(root, "a", "ignored"), "needle");
            File.WriteAllText(Path.Combine(root, "a", "main.mojo"), "needle");
            File.WriteAllText(Path.Combine(root, "b", "main.mojo"), "needle");
            string[] paths = [Path.Combine(root, "a"), Path.Combine(root, "b")];
            DirEntry[] files = WalkBuilder.FromPaths(paths).Build().Where(entry => entry.IsFile).ToArray();
            Require(files.Length == 2 && files.All(entry => entry.Length == 6), "root context and metadata");
            Require(WalkBuilder.FromPaths(paths.Reverse()).Build().Count(entry => entry.IsFile) == 2, "root order");
            var types = new FileTypeMatcherBuilder();
            types.AddDefaults();
            types.Select("mojo");
            Require(new WalkBuilder(root).FileTypes(types.Build()).Build().Count(entry => entry.IsFile) == 2, "file types");
            bool observed = false;
            Walk missing = new WalkBuilder(Path.Combine(root, "missing"))
                .ErrorHandler(error =>
                {
                    Require(error.Depth == 0 && error.InnerException is not null, "contextual error");
                    observed = true;
                    return WalkState.Continue;
                }).Build();
            Require(!missing.Any() && observed, "error callback");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        Console.WriteLine("Scout NuGet consumer passed.");
    }

    private static void Require(bool condition, string behavior)
    {
        if (!condition)
        {
            throw new InvalidOperationException(behavior);
        }
    }
}
