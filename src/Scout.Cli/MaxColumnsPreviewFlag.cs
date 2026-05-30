namespace Scout;

internal readonly struct MaxColumnsPreviewFlag : IFlag<MaxColumnsPreviewFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--max-columns-preview",
        shortName: null,
        "--no-max-columns-preview",
        aliases: [],
        FlagCategory.Output,
        "Print a preview for long matching lines.",
        static lowArgs =>
        {
            lowArgs.SetMaxColumnsPreview(true);
            return null;
        });
}
