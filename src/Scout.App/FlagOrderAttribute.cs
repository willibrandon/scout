using System;

namespace Scout;

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
internal sealed class FlagOrderAttribute : Attribute
{
    public FlagOrderAttribute(int order)
    {
        Order = order;
    }

    public int Order { get; }
}
