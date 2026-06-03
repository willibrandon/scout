
namespace Scout.Flags.Definitions;

[FlagOrder(44)]
internal readonly struct LineNumberNoFlag : IFlag<LineNumberNoFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-line-number",
        'N',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Suppress line numbers.",
        static lowArgs =>
        {
            lowArgs.SetLineNumber(false);
            return null;
        });
}
