namespace Scout;

internal readonly struct MaxDepthFlag : IFlag<MaxDepthFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--max-depth",
        'd',
        aliases: ["--maxdepth"],
        FlagCategory.Search,
        "Descend at most NUM directories.",
        static (lowArgs, value, matchedName) =>
        {
            _ = CliParser.ParseMaxDepth(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
