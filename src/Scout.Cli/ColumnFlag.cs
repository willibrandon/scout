namespace Scout;

internal readonly struct ColumnFlag : IFlag<ColumnFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--column",
        shortName: null,
        "--no-column",
        aliases: [],
        FlagCategory.Output,
        "Print column numbers with matching lines.",
        static lowArgs =>
        {
            lowArgs.SetColumn(true);
            return null;
        });
}
