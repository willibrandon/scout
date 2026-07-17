namespace Scout;

/// <summary>
/// Conservatively rejects start candidates whose next required end boundary cannot be preceded
/// by a byte accepted by the reversed regex automaton.
/// </summary>
/// <param name="allowedLastBytes">The lookup of bytes accepted immediately before the boundary.</param>
/// <param name="requiresTextEnd">Whether the required boundary is the end of the complete haystack.</param>
/// <param name="lineTerminator">The configured line terminator for a required line end.</param>
internal sealed class RegexEndCandidatePredicate(
    bool[] allowedLastBytes,
    bool requiresTextEnd,
    byte lineTerminator)
{
    private const ulong MaximumReverseNfaStates = 256;

    private readonly bool[] _allowedLastBytes = allowedLastBytes;
    private readonly bool _requiresTextEnd = requiresTextEnd;
    private readonly byte _lineTerminator = lineTerminator;

    /// <summary>
    /// Attempts to derive a conservative last-byte candidate predicate from the reversed NFA.
    /// </summary>
    /// <param name="root">The parsed regex syntax.</param>
    /// <param name="options">The effective compilation options.</param>
    /// <param name="requiredStartKind">The position constraint shared by every possible match.</param>
    /// <param name="predicate">Receives the predicate when one can reject end-boundary candidates.</param>
    /// <returns><see langword="true" /> when a useful predicate was created.</returns>
    internal static bool TryCreate(
        RegexSyntaxNode root,
        RegexCompileOptions options,
        RegexRequiredStartKind requiredStartKind,
        out RegexEndCandidatePredicate? predicate)
    {
        predicate = null;
        if (requiredStartKind == RegexRequiredStartKind.None ||
            !options.ExcludeLineTerminators ||
            options.Crlf ||
            RegexNfaCompiler.EstimateUnanchoredConstruction(root, options).ReverseStateCount >
                MaximumReverseNfaStates)
        {
            return false;
        }

        RegexNfa reversed = RegexNfaCompiler.CompileReversed(root, options);
        bool[] allowedLastBytes = new bool[byte.MaxValue + 1];
        Array.Fill(allowedLastBytes, value: true, startIndex: 0x80, count: 0x80);
        bool[] visitedWithoutBoundary = new bool[reversed.States.Count];
        bool[] visitedWithBoundary = new bool[reversed.States.Count];
        bool invalid = false;
        bool sawConsumer = false;
        bool hasEndConstraint = false;
        bool requiresTextEnd = true;
        byte lineTerminator = options.LineTerminator;

        Visit(reversed.StartState, sawEndBoundary: false);
        if (invalid || !sawConsumer || !hasEndConstraint || !HasRejectedByte(allowedLastBytes))
        {
            return false;
        }

        predicate = new RegexEndCandidatePredicate(
            allowedLastBytes,
            requiresTextEnd,
            lineTerminator);
        return true;

        void Visit(int stateIndex, bool sawEndBoundary)
        {
            if (invalid || stateIndex < 0)
            {
                return;
            }

            bool[] visited = sawEndBoundary ? visitedWithBoundary : visitedWithoutBoundary;
            if (visited[stateIndex])
            {
                return;
            }

            visited[stateIndex] = true;
            RegexNfaState state = reversed.States[stateIndex];
            switch (state.Kind)
            {
                case RegexNfaStateKind.Accept:
                    invalid = true;
                    return;

                case RegexNfaStateKind.Split:
                case RegexNfaStateKind.GreedyLoopSplit:
                case RegexNfaStateKind.LazyLoopSplit:
                    Visit(state.Next, sawEndBoundary);
                    Visit(state.Alternative, sawEndBoundary);
                    return;

                case RegexNfaStateKind.CaptureStart:
                case RegexNfaStateKind.CaptureEnd:
                    Visit(state.Next, sawEndBoundary);
                    return;

                case RegexNfaStateKind.Predicate:
                    bool nextSawEndBoundary = sawEndBoundary;
                    if (state.AtomKind is RegexSyntaxKind.StartAnchor or
                        RegexSyntaxKind.AbsoluteStartAnchor)
                    {
                        if (!TryMergeEndConstraint(state))
                        {
                            invalid = true;
                            return;
                        }

                        nextSawEndBoundary = true;
                    }

                    Visit(state.Next, nextSawEndBoundary);
                    return;

                case RegexNfaStateKind.Atom:
                    if (!sawEndBoundary)
                    {
                        invalid = true;
                        return;
                    }

                    sawConsumer = true;
                    AddAtomBytes(state, allowedLastBytes);
                    return;

                case RegexNfaStateKind.Sparse:
                    if (!sawEndBoundary)
                    {
                        invalid = true;
                        return;
                    }

                    sawConsumer = true;
                    AddSparseBytes(state, allowedLastBytes);
                    return;
            }
        }

        bool TryMergeEndConstraint(RegexNfaState state)
        {
            bool candidateRequiresTextEnd = state.AtomKind == RegexSyntaxKind.AbsoluteStartAnchor ||
                !state.MultiLine;
            if (!candidateRequiresTextEnd &&
                (state.Crlf || state.LineTerminator != options.ExcludedLineTerminator))
            {
                return false;
            }

            if (!hasEndConstraint)
            {
                hasEndConstraint = true;
                requiresTextEnd = candidateRequiresTextEnd;
                lineTerminator = state.LineTerminator;
                return true;
            }

            if (requiresTextEnd && candidateRequiresTextEnd)
            {
                return true;
            }

            if (!requiresTextEnd && !candidateRequiresTextEnd &&
                lineTerminator != state.LineTerminator)
            {
                return false;
            }

            if (requiresTextEnd && !candidateRequiresTextEnd)
            {
                lineTerminator = state.LineTerminator;
            }

            requiresTextEnd = false;
            return true;
        }
    }

    /// <summary>
    /// Determines whether the byte before the next required end boundary can complete a match.
    /// </summary>
    /// <param name="haystack">The complete search window.</param>
    /// <param name="start">The proposed match start.</param>
    /// <returns><see langword="true" /> when the boundary remains a possible match end.</returns>
    internal bool CanMatchAtNextBoundary(ReadOnlySpan<byte> haystack, int start)
    {
        if ((uint)start > (uint)haystack.Length)
        {
            return false;
        }

        int boundary = haystack.Length;
        if (!_requiresTextEnd)
        {
            int offset = haystack[start..].IndexOf(_lineTerminator);
            if (offset >= 0)
            {
                boundary = start + offset;
            }
        }

        return boundary > start && _allowedLastBytes[haystack[boundary - 1]];
    }

    private static void AddAtomBytes(RegexNfaState state, bool[] bytes)
    {
        for (int value = 0; value < 0x80; value++)
        {
            if (state.AtomMatches((byte)value))
            {
                bytes[value] = true;
            }
        }
    }

    private static void AddSparseBytes(RegexNfaState state, bool[] bytes)
    {
        for (int value = 0; value < 0x80; value++)
        {
            if (state.TryGetSparseTarget((byte)value, out _))
            {
                bytes[value] = true;
            }
        }
    }

    private static bool HasRejectedByte(bool[] bytes)
    {
        for (int value = 0; value < 0x80; value++)
        {
            if (!bytes[value])
            {
                return true;
            }
        }

        return false;
    }
}
