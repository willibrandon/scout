using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(27)]
internal readonly struct GenerateFlag : IFlag<GenerateFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--generate",
        shortName: null,
        aliases: [],
        FlagCategory.Output,
        "Generate a manual page or shell completions.",
        static (lowArgs, value, _) =>
        {
            CliParser.ParseGenerate(value, lowArgs, out ScoutError? error);
            return error;
        });
}
