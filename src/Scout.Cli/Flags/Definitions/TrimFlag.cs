using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct TrimFlag : IFlag<TrimFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--trim",
        shortName: null,
        "--no-trim",
        aliases: [],
        FlagCategory.Output,
        "Trim leading ASCII whitespace from printed lines.",
        static lowArgs =>
        {
            lowArgs.SetTrim(true);
            return null;
        });
}
