namespace Scout;

internal readonly struct LineRegexpFlag : IFlag<LineRegexpFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--line-regexp",
        'x',
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Require matches to span the entire line.",
        static lowArgs =>
        {
            lowArgs.SetLineRegexp(true);
            return null;
        });
}
