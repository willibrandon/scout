namespace Scout;

internal sealed class RegexRequiredByteSet
{
    private readonly bool[] bytes = new bool[256];

    public int Count { get; private set; }

    public void Add(byte value)
    {
        if (bytes[value])
        {
            return;
        }

        bytes[value] = true;
        Count++;
    }

    public void UnionWith(RegexRequiredByteSet other)
    {
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (other.Contains((byte)value))
            {
                Add((byte)value);
            }
        }
    }

    public byte[] ToArray()
    {
        byte[] values = new byte[Count];
        int index = 0;
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            if (Contains((byte)value))
            {
                values[index++] = (byte)value;
            }
        }

        return values;
    }

    private bool Contains(byte value)
    {
        return bytes[value];
    }
}
