using System.Text;

namespace Scout;

/// <summary>
/// Provides process environment variable access without losing Unix byte fidelity.
/// </summary>
public static unsafe class ProcessEnvironment
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static byte[][]? activeUnixEnvironment;

    /// <summary>
    /// Captures a Unix <c>envp</c> vector into managed byte arrays.
    /// </summary>
    /// <param name="envp">A null-terminated vector of null-terminated environment entries.</param>
    /// <returns>The captured environment entries without trailing NUL bytes.</returns>
    public static byte[][] CaptureUnix(byte** envp)
    {
        if (envp is null)
        {
            return [];
        }

        var entries = new List<byte[]>();
        for (nuint index = 0; envp[index] is not null; index++)
        {
            byte* entry = envp[index];
            int length = MeasureNullTerminated(entry);
            entries.Add(new ReadOnlySpan<byte>(entry, length).ToArray());
        }

        return entries.ToArray();
    }

    /// <summary>
    /// Routes future environment lookups through a captured Unix <c>envp</c> vector.
    /// </summary>
    /// <param name="envp">A null-terminated vector of null-terminated environment entries.</param>
    public static void UseUnixEnvironment(byte** envp)
    {
        activeUnixEnvironment = CaptureUnix(envp);
    }

    /// <summary>
    /// Routes future environment lookups through the managed runtime process environment.
    /// </summary>
    public static void UseCurrentProcessEnvironment()
    {
        activeUnixEnvironment = null;
    }

    /// <summary>
    /// Gets an environment variable from the active process environment.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable value, or <see langword="null" /> when absent or not valid UTF-8 on Unix.</returns>
    public static string? GetVariable(string name)
    {
        return GetVariableOsString(name) is { } value && value.TryGetText(out string text)
            ? text
            : null;
    }

    /// <summary>
    /// Gets an environment variable from the active process environment without losing Unix bytes.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable value, or <see langword="null" /> when absent.</returns>
    public static OsString? GetVariableOsString(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        byte[][]? unixEnvironment = activeUnixEnvironment;
        return unixEnvironment is null
            ? GetCurrentProcessVariable(name)
            : GetVariableOsString(unixEnvironment, name);
    }

    /// <summary>
    /// Gets an environment variable from captured Unix environment entries.
    /// </summary>
    /// <param name="unixEnvironment">The captured Unix environment entries.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable value, or <see langword="null" /> when absent or not valid UTF-8.</returns>
    public static string? GetVariable(IReadOnlyList<byte[]> unixEnvironment, string name)
    {
        return GetVariableOsString(unixEnvironment, name) is { } value && value.TryGetText(out string text)
            ? text
            : null;
    }

    /// <summary>
    /// Gets an environment variable from captured Unix environment entries without decoding the value.
    /// </summary>
    /// <param name="unixEnvironment">The captured Unix environment entries.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable value, or <see langword="null" /> when absent.</returns>
    public static OsString? GetVariableOsString(IReadOnlyList<byte[]> unixEnvironment, string name)
    {
        ArgumentNullException.ThrowIfNull(unixEnvironment);
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0 || name.Contains('=', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }

        byte[] nameBytes = Utf8.GetBytes(name);
        for (int index = 0; index < unixEnvironment.Count; index++)
        {
            ReadOnlySpan<byte> entry = unixEnvironment[index];
            if (entry.Length <= nameBytes.Length ||
                entry[nameBytes.Length] != (byte)'=' ||
                !entry[..nameBytes.Length].SequenceEqual(nameBytes))
            {
                continue;
            }

            return OsString.FromUnixBytes(entry[(nameBytes.Length + 1)..]);
        }

        return null;
    }

    private static OsString? GetCurrentProcessVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return value is null ? null : OsString.FromText(value);
    }

    private static int MeasureNullTerminated(byte* pointer)
    {
        nuint length = 0;
        while (pointer[length] != 0)
        {
            length++;
            if (length > int.MaxValue)
            {
                throw new ArgumentException("Environment entry is too large to address as a managed span.", nameof(pointer));
            }
        }

        return checked((int)length);
    }
}
