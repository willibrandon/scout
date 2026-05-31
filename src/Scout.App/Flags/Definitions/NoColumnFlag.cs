using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct NoColumnFlag : IFlag<NoColumnFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-column",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Disable column numbers in matching output.",
        static lowArgs =>
        {
            lowArgs.SetColumn(false);
            return null;
        });
}
