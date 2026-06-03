
namespace Scout;

internal static class GeneratedTextOutput
{
    internal static byte[] ForCurrentPlatform(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!OperatingSystem.IsWindows())
        {
            return bytes;
        }

        int extraCarriageReturns = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\n' && (index == 0 || bytes[index - 1] != (byte)'\r'))
            {
                extraCarriageReturns++;
            }
        }

        if (extraCarriageReturns == 0)
        {
            return bytes;
        }

        byte[] normalized = new byte[bytes.Length + extraCarriageReturns];
        int outputIndex = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\n' && (index == 0 || bytes[index - 1] != (byte)'\r'))
            {
                normalized[outputIndex++] = (byte)'\r';
            }

            normalized[outputIndex++] = bytes[index];
        }

        return normalized;
    }
}
