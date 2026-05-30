using System.Text;

namespace Scout;

internal static class EncodingUtf8
{
    public static readonly Encoding Value = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static byte[] GetBytes(string text)
    {
        return Value.GetBytes(text);
    }
}
