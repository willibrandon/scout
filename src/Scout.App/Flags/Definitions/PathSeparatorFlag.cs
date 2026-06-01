using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(70)]
internal readonly struct PathSeparatorFlag : IFlag<PathSeparatorFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--path-separator",
        shortName: null,
        aliases: [],
        FlagCategory.Output,
        "Set the path separator for printed paths.",
        static (lowArgs, value, _) =>
        {
            CliParser.ParsePathSeparator(value, lowArgs, out ScoutError? error);
            return error;
        });
}
