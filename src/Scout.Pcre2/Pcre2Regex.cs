using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Scout;

/// <summary>
/// Wraps a compiled 8-bit PCRE2 pattern.
/// </summary>
public sealed unsafe partial class Pcre2Regex : IDisposable
{
    private const int ErrorNoMatch = -1;
    private const uint JitComplete = 0x00000001;

    private IntPtr code;
    private IntPtr matchData;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pcre2Regex" /> class.
    /// </summary>
    /// <param name="pattern">The UTF-8 PCRE2 pattern bytes.</param>
    /// <param name="options">The compile options.</param>
    public Pcre2Regex(ReadOnlySpan<byte> pattern, Pcre2CompileOptions options)
    {
        if (!Pcre2Library.IsAvailable)
        {
            throw new InvalidOperationException(Pcre2Library.UnavailableErrorMessage);
        }

        int errorCode = 0;
        nuint errorOffset = 0;
        fixed (byte* patternPointer = pattern)
        {
            code = Pcre2Compile(
                patternPointer,
                (nuint)pattern.Length,
                (uint)options,
                &errorCode,
                &errorOffset,
                IntPtr.Zero);
        }

        if (code == IntPtr.Zero)
        {
            throw CreateException(errorCode, errorOffset, "compile");
        }

        bool initialized = false;
        try
        {
            matchData = Pcre2MatchDataCreateFromPattern(code, IntPtr.Zero);
            if (matchData == IntPtr.Zero)
            {
                throw CreateException(errorCode: -1, offset: 0, operation: "match-data allocation");
            }

            if (Pcre2Library.IsJitAvailable)
            {
                _ = Pcre2JitCompile(code, JitComplete);
            }

            initialized = true;
        }
        finally
        {
            if (!initialized)
            {
                Dispose();
            }
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="Pcre2Regex" /> class.
    /// </summary>
    ~Pcre2Regex()
    {
        DisposeCore();
    }

    /// <summary>
    /// Finds the first match in the subject.
    /// </summary>
    /// <param name="subject">The subject bytes.</param>
    /// <param name="match">The first match span.</param>
    /// <returns><see langword="true" /> when a match was found.</returns>
    public bool TryFind(ReadOnlySpan<byte> subject, out Pcre2Match match)
    {
        ThrowIfDisposed();
        int result;
        if (subject.Length == 0)
        {
            byte empty = 0;
            result = Pcre2Match(
                code,
                &empty,
                0,
                0,
                options: 0,
                matchData,
                IntPtr.Zero);
        }
        else
        {
            fixed (byte* subjectPointer = subject)
            {
                result = Pcre2Match(
                    code,
                    subjectPointer,
                    (nuint)subject.Length,
                    0,
                    options: 0,
                    matchData,
                    IntPtr.Zero);
            }
        }

        if (result == ErrorNoMatch)
        {
            match = default;
            return false;
        }

        if (result < 0)
        {
            throw CreateException(result, offset: 0, operation: "match");
        }

        nuint* offsets = Pcre2GetOvectorPointer(matchData);
        match = new Pcre2Match(checked((int)offsets[0]), checked((int)(offsets[1] - offsets[0])));
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    private static Pcre2Exception CreateException(int errorCode, nuint offset, string operation)
    {
        return Pcre2Exception.Create(errorCode, offset, operation, GetErrorMessage(errorCode));
    }

    private static string GetErrorMessage(int errorCode)
    {
        Span<byte> buffer = stackalloc byte[256];
        fixed (byte* bufferPointer = buffer)
        {
            int length = Pcre2GetErrorMessage(errorCode, bufferPointer, (nuint)buffer.Length);
            if (length >= 0)
            {
                return Encoding.UTF8.GetString(buffer[..length]);
            }
        }

        return "PCRE2 error " + errorCode.ToString(CultureInfo.InvariantCulture);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(code == IntPtr.Zero, this);
    }

    private void DisposeCore()
    {
        IntPtr data = matchData;
        matchData = IntPtr.Zero;
        if (data != IntPtr.Zero)
        {
            Pcre2MatchDataFree(data);
        }

        IntPtr compiled = code;
        code = IntPtr.Zero;
        if (compiled != IntPtr.Zero)
        {
            Pcre2CodeFree(compiled);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_compile_8")]
    private static partial IntPtr Pcre2Compile(
        byte* pattern,
        nuint length,
        uint options,
        int* errorCode,
        nuint* errorOffset,
        IntPtr compileContext);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_code_free_8")]
    private static partial void Pcre2CodeFree(IntPtr code);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_match_8")]
    private static partial int Pcre2Match(
        IntPtr code,
        byte* subject,
        nuint length,
        nuint startOffset,
        uint options,
        IntPtr matchData,
        IntPtr matchContext);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_match_data_create_from_pattern_8")]
    private static partial IntPtr Pcre2MatchDataCreateFromPattern(IntPtr code, IntPtr generalContext);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_match_data_free_8")]
    private static partial void Pcre2MatchDataFree(IntPtr matchData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_get_ovector_pointer_8")]
    private static partial nuint* Pcre2GetOvectorPointer(IntPtr matchData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_jit_compile_8")]
    private static partial int Pcre2JitCompile(IntPtr code, uint options);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_get_error_message_8")]
    private static partial int Pcre2GetErrorMessage(int errorCode, byte* buffer, nuint bufferLength);
}
