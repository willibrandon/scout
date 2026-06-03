
namespace Scout.Flags.Definitions;

[FlagOrder(1)]
internal readonly struct FileFlag : IFlag<FileFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--file",
        'f',
        aliases: [],
        FlagCategory.Matching,
        "Read patterns from a file.",
        static (lowArgs, value, _) =>
        {
            CliParser.ParsePatternFile(value, lowArgs, out ScoutError? error);
            return error;
        });
}
