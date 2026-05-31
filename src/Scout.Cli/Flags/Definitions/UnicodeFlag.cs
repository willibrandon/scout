using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct UnicodeFlag : IFlag<UnicodeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--unicode",
        shortName: null,
        "--no-unicode",
        aliases: [],
        FlagCategory.Regex,
        "Enable Unicode regex mode.",
        static lowArgs =>
        {
            lowArgs.SetUnicode(true);
            return null;
        });
}
