using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct JsonFlag : IFlag<JsonFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--json",
        shortName: null,
        "--no-json",
        aliases: [],
        FlagCategory.Output,
        "Emit JSON Lines output.",
        static (lowArgs, matchedName) =>
        {
            if (matchedName == "--no-json")
            {
                lowArgs.ClearJsonMode();
            }
            else
            {
                lowArgs.SetSearchMode(CliSearchMode.Json);
            }

            return null;
        });
}
