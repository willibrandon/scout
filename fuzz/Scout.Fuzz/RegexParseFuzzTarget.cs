
namespace Scout;

internal static class RegexParseFuzzTarget
{
    internal static void Run(ReadOnlySpan<byte> input)
    {
        try
        {
            _ = RegexSyntaxParser.Parse(input);
        }
        catch (FormatException)
        {
        }
    }
}
