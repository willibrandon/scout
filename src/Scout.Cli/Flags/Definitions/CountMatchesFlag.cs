namespace Scout.Flags.Definitions;

internal readonly struct CountMatchesFlag : IFlag<CountMatchesFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--count-matches",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Only print the count of individual matches for each file.",
        static lowArgs =>
        {
            lowArgs.SetSearchMode(CliSearchMode.CountMatches);
            return null;
        });
}
