namespace Scout;

internal readonly struct RegexSizeLimitFlag : IFlag<RegexSizeLimitFlag>
{
    public static FlagDescriptor Descriptor { get; } = FlagDescriptor.Value(
        "--regex-size-limit",
        shortName: null,
        aliases: [],
        FlagCategory.Regex,
        "Set the compiled regex size limit.",
        static (lowArgs, value, matchedName) =>
        {
            CliParser.ParseRegexSizeLimit(value, matchedName, lowArgs, out ScoutError? error);
            return error;
        });
}
