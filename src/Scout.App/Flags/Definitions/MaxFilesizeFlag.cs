using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(50)]
internal readonly struct MaxFilesizeFlag : IFlag<MaxFilesizeFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--max-filesize",
        shortName: null,
        aliases: [],
        FlagCategory.Search,
        "Skip files larger than the given size.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseMaxFileSize(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
