
namespace Scout.Flags.Definitions;

[FlagOrder(76)]
internal readonly struct PrettyFlag : IFlag<PrettyFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Switch(
        "--pretty",
        'p',
        negatedName: null,
        aliases: [],
        FlagCategory.Output,
        "Apply ripgrep's pretty output alias.",
        static lowArgs =>
        {
            lowArgs.SetPretty();
            return null;
        });
}
