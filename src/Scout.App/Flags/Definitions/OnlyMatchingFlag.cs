
namespace Scout.Flags.Definitions;

[FlagOrder(69)]
internal readonly struct OnlyMatchingFlag : IFlag<OnlyMatchingFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--only-matching",
        'o',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Print only the matching portions of lines.",
        static lowArgs =>
        {
            lowArgs.SetOnlyMatching(true);
            return null;
        });
}
