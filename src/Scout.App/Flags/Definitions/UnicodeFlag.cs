using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct UnicodeFlag : IFlag<UnicodeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--unicode",
        shortName: null,
        "--no-unicode",
        aliases: [],
        FlagCategory.Regex,
        "Enable Unicode regex mode.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetUnicode(matchedName != "--no-unicode");
            return null;
        });
}
