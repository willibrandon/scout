using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NullDataFlag : IFlag<NullDataFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--null-data",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Matching,
        "Treat NUL as a line terminator.",
        static lowArgs =>
        {
            lowArgs.SetNullData(true);
            return null;
        });
}
