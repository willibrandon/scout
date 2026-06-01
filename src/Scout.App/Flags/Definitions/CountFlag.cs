using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(13)]
internal readonly struct CountFlag : IFlag<CountFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--count",
        'c',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Only print the count of matching lines for each file.",
        static lowArgs =>
        {
            lowArgs.SetSearchMode(CliSearchMode.Count);
            return null;
        });
}
