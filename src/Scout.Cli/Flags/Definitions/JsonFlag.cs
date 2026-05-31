using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct JsonFlag : IFlag<JsonFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--json",
        shortName: null,
        "--no-json",
        aliases: [],
        FlagCategory.Output,
        "Emit JSON Lines output.",
        static lowArgs =>
        {
            lowArgs.SetSearchMode(CliSearchMode.Json);
            return null;
        });
}
