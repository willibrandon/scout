
namespace Scout.Flags.Definitions;

[FlagOrder(79)]
internal readonly struct ReplaceFlag : IFlag<ReplaceFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--replace",
        'r',
        aliases: [],
        FlagCategory.Output,
        "Replace matching text in output.",
        static (lowArgs, value, _) =>
        {
            CliParser.ParseReplacement(value, lowArgs, out ScoutError? error);
            return error;
        });
}
