using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(100)]
internal readonly struct WordRegexpFlag : IFlag<WordRegexpFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--word-regexp",
        'w',
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Require matches to be bounded by word boundaries.",
        static lowArgs =>
        {
            lowArgs.SetWordRegexp(true);
            return null;
        });
}
