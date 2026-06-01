using Scout;

namespace Scout.Flags.Definitions;

[FlagOrder(47)]
internal readonly struct MaxColumnsPreviewFlag : IFlag<MaxColumnsPreviewFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.SwitchWithName(
        "--max-columns-preview",
        shortName: null,
        "--no-max-columns-preview",
        aliases: [],
        FlagCategory.Output,
        "Print a preview for long matching lines.",
        static (lowArgs, matchedName) =>
        {
            lowArgs.SetMaxColumnsPreview(matchedName != "--no-max-columns-preview");
            return null;
        });
}
