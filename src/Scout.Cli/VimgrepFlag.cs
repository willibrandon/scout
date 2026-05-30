namespace Scout;

internal readonly struct VimgrepFlag : IFlag<VimgrepFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--vimgrep",
        shortName: null,
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Print matches in vim-compatible format.",
        static lowArgs =>
        {
            lowArgs.SetVimgrep(true);
            return null;
        });
}
