
namespace Scout.Flags.Definitions;

[FlagOrder(77)]
internal readonly struct QuietFlag : IFlag<QuietFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--quiet",
        'q',
        negatedName: null,
        aliases: [],
        FlagCategory.Diagnostics,
        "Suppress stdout output.",
        static lowArgs =>
        {
            lowArgs.SetQuiet(true);
            return null;
        });
}
