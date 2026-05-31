using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct VersionFlag : IFlag<VersionFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Special(
        "--version",
        'V',
        aliases: [],
        FlagCategory.Diagnostics,
        "Print version information.",
        static matchedName => matchedName == "-V"
            ? CliSpecialMode.VersionShort
            : CliSpecialMode.VersionLong);
}
