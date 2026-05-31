using Scout;

namespace Scout.Flags.Definitions;

internal readonly struct UnrestrictedFlag : IFlag<UnrestrictedFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--unrestricted",
        'u',
        negatedName: null,
        aliases: [],
        FlagCategory.Search,
        "Reduce ignore filtering.",
        static (lowArgs, matchedName) =>
        {
            if (lowArgs.UnrestrictedCount >= 3)
            {
                return new ScoutError($"error parsing flag {matchedName}: flag can only be repeated up to 3 times");
            }

            lowArgs.AddUnrestrictedLevel();
            return null;
        });
}
