namespace Scout;

internal readonly struct NoMaxColumnsPreviewFlag : IFlag<NoMaxColumnsPreviewFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--no-max-columns-preview",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Disable previews for long matching lines.",
        static lowArgs =>
        {
            lowArgs.SetMaxColumnsPreview(false);
            return null;
        });
}
