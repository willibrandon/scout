using System.Collections.ObjectModel;
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
    private const uint InfoCaptureCount = 4;
    private const uint InfoNameCount = 17;
    private const uint InfoNameEntrySize = 18;
    private const uint InfoNameTable = 19;
    private const uint JitComplete = 0x00000001;

    private readonly int _captureCount;
    private readonly IReadOnlyDictionary<string, int> _captureNames;
    private IntPtr _code;
    private IntPtr _matchData;

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
            _code = Pcre2Compile(
                patternPointer,
                (nuint)pattern.Length,
                (uint)options,
                &errorCode,
                &errorOffset,
                IntPtr.Zero);
        }

        if (_code == IntPtr.Zero)
        {
            throw CreateException(errorCode, errorOffset, "compile");
        }

        bool initialized = false;
        try
        {
            _matchData = Pcre2MatchDataCreateFromPattern(_code, IntPtr.Zero);
            if (_matchData == IntPtr.Zero)
            {
                throw CreateException(errorCode: -1, offset: 0, operation: "match-data allocation");
            }

            _captureCount = GetCaptureCount(_code);
            _captureNames = GetCaptureNames(_code);
            if (Pcre2Library.IsJitAvailable)
            {
                _ = Pcre2JitCompile(_code, JitComplete);
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
    /// Gets the number of capturing groups in the compiled expression.
    /// </summary>
    internal int CaptureCount => _captureCount;

    /// <summary>
    /// Gets the number of flattened start and exclusive-end capture slots.
    /// </summary>
    internal int CaptureSlotCount => checked(2 * (_captureCount + 1));

    /// <summary>
    /// Gets the capture indexes keyed by capture name.
    /// </summary>
    internal IReadOnlyDictionary<string, int> CaptureNames => _captureNames;

    /// <summary>
    /// Finds the first match in the subject.
    /// </summary>
    /// <param name="subject">The subject bytes.</param>
    /// <param name="match">The first match span.</param>
    /// <returns><see langword="true" /> when a match was found.</returns>
    public bool TryFind(ReadOnlySpan<byte> subject, out Pcre2Match match)
    {
        return TryFind(subject, startOffset: 0, out match);
    }

    /// <summary>
    /// Finds the first match in the subject at or after the given start offset.
    /// </summary>
    /// <param name="subject">The subject bytes.</param>
    /// <param name="startOffset">The zero-based subject offset where matching starts.</param>
    /// <param name="match">The first match span.</param>
    /// <returns><see langword="true" /> when a match was found.</returns>
    public bool TryFind(ReadOnlySpan<byte> subject, int startOffset, out Pcre2Match match)
    {
        ThrowIfDisposed();
        if (startOffset < 0 || startOffset > subject.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        }

        int result;
        if (subject.Length == 0)
        {
            byte empty = 0;
            result = Pcre2Match(
                _code,
                &empty,
                0,
                (nuint)startOffset,
                options: 0,
                _matchData,
                IntPtr.Zero);
        }
        else
        {
            fixed (byte* subjectPointer = subject)
            {
                result = Pcre2Match(
                    _code,
                    subjectPointer,
                    (nuint)subject.Length,
                    (nuint)startOffset,
                    options: 0,
                    _matchData,
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

        nuint* offsets = Pcre2GetOvectorPointer(_matchData);
        match = CreateMatch(
            offsets[0],
            offsets[1],
            Pcre2GetStartCharacter(_matchData));
        return true;
    }

    /// <summary>
    /// Creates a managed match span from authoritative PCRE2 offsets.
    /// </summary>
    /// <param name="start">The reported match start.</param>
    /// <param name="end">The reported exclusive match end.</param>
    /// <param name="patternStart">The pattern start before any <c>\K</c> adjustment.</param>
    /// <returns>The managed match span.</returns>
    /// <exception cref="Pcre2Exception">PCRE2 reported a start after the exclusive end.</exception>
    internal static Pcre2Match CreateMatch(
        nuint start,
        nuint end,
        nuint patternStart)
    {
        if (start > end)
        {
            throw new Pcre2Exception(
                "PCRE2 reported a match start after its exclusive end. " +
                "Scout's PCRE2 policy rejects lookaround \\K during compilation.");
        }

        return new Pcre2Match(
            checked((int)start),
            checked((int)(end - start)),
            checked((int)patternStart));
    }

    /// <summary>
    /// Replays one selected match and copies its authoritative PCRE2 capture slots.
    /// </summary>
    /// <param name="subject">The complete subject used by the search.</param>
    /// <param name="matchStart">The reported match start.</param>
    /// <param name="matchLength">The reported match length.</param>
    /// <param name="searchStart">The start of the successful PCRE2 match attempt.</param>
    /// <param name="captureSlots">Receives absolute start and exclusive-end capture offsets.</param>
    /// <returns><see langword="true" /> when PCRE2 reproduces the selected match.</returns>
    internal bool TryCollectCaptureSlots(
        ReadOnlySpan<byte> subject,
        int matchStart,
        int matchLength,
        int searchStart,
        Span<int> captureSlots)
    {
        if ((uint)matchStart > (uint)subject.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(matchStart));
        }

        if (matchLength < 0 || matchLength > subject.Length - matchStart)
        {
            throw new ArgumentOutOfRangeException(nameof(matchLength));
        }

        if ((uint)searchStart > (uint)matchStart)
        {
            throw new ArgumentOutOfRangeException(nameof(searchStart));
        }

        if (captureSlots.Length < CaptureSlotCount)
        {
            throw new ArgumentException("The capture slot buffer is too small.", nameof(captureSlots));
        }

        captureSlots.Fill(-1);
        if (!TryFind(subject, searchStart, out Pcre2Match replayed) ||
            replayed.Start != matchStart ||
            replayed.Length != matchLength)
        {
            return false;
        }

        nuint* offsets = Pcre2GetOvectorPointer(_matchData);
        for (int index = 0; index <= _captureCount; index++)
        {
            nuint start = offsets[2 * index];
            nuint end = offsets[(2 * index) + 1];
            if (start != nuint.MaxValue && end != nuint.MaxValue)
            {
                captureSlots[2 * index] = checked((int)start);
                captureSlots[(2 * index) + 1] = checked((int)end);
            }
        }

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

    private static int GetCaptureCount(IntPtr code)
    {
        uint captureCount = 0;
        ThrowIfPatternInfoFailed(Pcre2PatternInfo(code, InfoCaptureCount, &captureCount));
        return checked((int)captureCount);
    }

    private static ReadOnlyDictionary<string, int> GetCaptureNames(IntPtr code)
    {
        uint nameCount;
        System.Runtime.CompilerServices.Unsafe.SkipInit(out nameCount);
        ThrowIfPatternInfoFailed(Pcre2PatternInfo(code, InfoNameCount, &nameCount));
        var names = new Dictionary<string, int>(checked((int)nameCount), StringComparer.Ordinal);
        if (nameCount == 0)
        {
            return new ReadOnlyDictionary<string, int>(names);
        }

        uint entrySize;
        nint nameTableAddress;
        System.Runtime.CompilerServices.Unsafe.SkipInit(out entrySize);
        System.Runtime.CompilerServices.Unsafe.SkipInit(out nameTableAddress);
        ThrowIfPatternInfoFailed(Pcre2PatternInfo(code, InfoNameEntrySize, &entrySize));
        ThrowIfPatternInfoFailed(Pcre2PatternInfo(code, InfoNameTable, &nameTableAddress));
        byte* nameTable = (byte*)nameTableAddress;
        for (uint index = 0; index < nameCount; index++)
        {
            byte* entry = nameTable + checked((int)(index * entrySize));
            int captureIndex = (entry[0] << 8) | entry[1];
            int maximumNameLength = checked((int)entrySize) - 2;
            int nameLength = 0;
            while (nameLength < maximumNameLength && entry[nameLength + 2] != 0)
            {
                nameLength++;
            }

            string name = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(entry + 2, nameLength));
            names[name] = captureIndex;
        }

        return new ReadOnlyDictionary<string, int>(names);
    }

    private static void ThrowIfPatternInfoFailed(int result)
    {
        if (result != 0)
        {
            throw CreateException(result, offset: 0, operation: "pattern information");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_code == IntPtr.Zero, this);
    }

    private void DisposeCore()
    {
        IntPtr data = _matchData;
        _matchData = IntPtr.Zero;
        if (data != IntPtr.Zero)
        {
            Pcre2MatchDataFree(data);
        }

        IntPtr compiled = _code;
        _code = IntPtr.Zero;
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
    [LibraryImport("__Internal", EntryPoint = "pcre2_get_startchar_8")]
    private static partial nuint Pcre2GetStartCharacter(IntPtr matchData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_pattern_info_8")]
    private static partial int Pcre2PatternInfo(IntPtr code, uint what, void* where);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_jit_compile_8")]
    private static partial int Pcre2JitCompile(IntPtr code, uint options);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("__Internal", EntryPoint = "pcre2_get_error_message_8")]
    private static partial int Pcre2GetErrorMessage(int errorCode, byte* buffer, nuint bufferLength);
}
