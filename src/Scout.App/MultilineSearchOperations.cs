
namespace Scout;

internal static class MultilineSearchOperations
{
    internal static bool TrySearchBytes(
        ReadOnlySpan<byte> searchSpan,
        ReadOnlySpan<byte> outputSpan,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool vimgrep,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool quiet,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool includeZero,
        bool nullPathTerminator,
        out bool matched)
    {
        matched = false;
        bool contextOutputRequested = (beforeContext > 0 || afterContext > 0 || passthru) &&
            searchMode == CliSearchMode.Standard;
        if (separators.Crlf ||
            separators.NullData)
        {
            return false;
        }

        if (contextOutputRequested)
        {
            matched = SearchMultilineContextBytes(searchSpan, outputSpan, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, asciiCaseInsensitive, invertMatch, lineRegexp, wordRegexp, multilineDotall, vimgrep, onlyMatching, replacement, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator);
            return true;
        }

        if (invertMatch)
        {
            matched = SearchMultilineInvertedBytes(searchSpan, patterns, output, prefix, separators, lineLimit, color, searchMode, lineNumber, column, byteOffset, maxCount, quiet, trim, includeZero, nullPathTerminator, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
            return true;
        }

        if (!vimgrep &&
            replacement is null &&
            !onlyMatching &&
            TryCreateSignatureArityAccelerator(patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, out SignatureArityAccelerator? signatureAccelerator))
        {
            return SearchSignatureArityBytes(searchSpan, outputSpan, signatureAccelerator!, output, prefix, separators, lineLimit, color, searchMode, lineNumber, column, byteOffset, maxCount, quiet, trim, includeZero, nullPathTerminator, out matched);
        }

        RegexAutomaton[] automata = CompileMultilineAutomata(patterns, asciiCaseInsensitive, multilineDotall);
        if (!TryFindMultilineMatch(searchSpan, automata, lineRegexp, wordRegexp, startAt: 0, out RegexMatch firstMatch) ||
            IsTrailingEmptyLineMatch(searchSpan, firstMatch))
        {
            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                matched = SearchOutputFormatting.WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
            {
                matched = SearchOutputFormatting.WriteCount(output, prefix, color, 0, includeZero, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            return true;
        }

        if (quiet)
        {
            matched = searchMode != CliSearchMode.FilesWithoutMatch;
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            matched = SearchOutputFormatting.WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            matched = SearchOutputFormatting.WritePathIf(output, prefix, color, false, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            long count = CountMultilineMatches(searchSpan, automata, lineRegexp, wordRegexp, maxCount);
            matched = SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        matched = true;
        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            if (onlyMatching)
            {
                WriteMultilineOnlyMatchingReplacements(outputSpan, patterns, automata, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
                return true;
            }

            WriteMultilineReplacedLines(outputSpan, patterns, automata, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount);
            return true;
        }

        if (onlyMatching && !vimgrep)
        {
            WriteMultilineOnlyMatches(outputSpan, automata, output, prefix, separators, color, lineNumber, column, byteOffset, trim, nullPathTerminator, lineRegexp, wordRegexp, maxCount);
            return true;
        }

        if (vimgrep)
        {
            if (onlyMatching)
            {
                WriteMultilineOnlyMatches(outputSpan, automata, output, prefix, separators, color, lineNumber: true, column: true, byteOffset, trim, nullPathTerminator, lineRegexp, wordRegexp, maxCount);
                return true;
            }

            WriteMultilineVimgrepMatches(outputSpan, automata, output, prefix, separators, lineLimit, color, trim, nullPathTerminator, lineRegexp, wordRegexp, maxCount);
            return true;
        }

        WriteMultilineMatchedLines(outputSpan, automata, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, lineRegexp, wordRegexp, maxCount);
        return true;
    }

    private static bool SearchSignatureArityBytes(
        ReadOnlySpan<byte> searchSpan,
        ReadOnlySpan<byte> outputSpan,
        SignatureArityAccelerator accelerator,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool lineNumber,
        bool column,
        bool byteOffset,
        ulong? maxCount,
        bool quiet,
        bool trim,
        bool includeZero,
        bool nullPathTerminator,
        out bool matched)
    {
        matched = false;
        if (!accelerator.TryFindNext(searchSpan, startAt: 0, out RegexMatch firstMatch))
        {
            if (searchMode == CliSearchMode.FilesWithoutMatch)
            {
                matched = SearchOutputFormatting.WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
            {
                matched = SearchOutputFormatting.WriteCount(output, prefix, color, 0, includeZero, nullPathTerminator, separators.LineTerminator);
                return true;
            }

            return true;
        }

        if (quiet)
        {
            matched = searchMode != CliSearchMode.FilesWithoutMatch;
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            matched = SearchOutputFormatting.WritePathIf(output, prefix, color, true, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            matched = SearchOutputFormatting.WritePathIf(output, prefix, color, false, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            long count = accelerator.Count(searchSpan, maxCount);
            matched = SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
            return true;
        }

        matched = true;
        WriteSignatureArityMatchedLines(outputSpan, accelerator, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, maxCount, firstMatch);
        return true;
    }

    private static bool SearchMultilineInvertedBytes(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        CliSearchMode searchMode,
        bool lineNumber,
        bool column,
        bool byteOffset,
        ulong? maxCount,
        bool quiet,
        bool trim,
        bool includeZero,
        bool nullPathTerminator,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall)
    {
        List<ContextLineInfo> lines = BuildMultilineInvertedLines(bytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
        long count = CountSelectedMultilineLines(lines, maxCount);
        bool hasSelectedMatch = count > 0;

        if (quiet)
        {
            return searchMode == CliSearchMode.FilesWithoutMatch ? !hasSelectedMatch : hasSelectedMatch;
        }

        if (searchMode == CliSearchMode.FilesWithMatches)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.FilesWithoutMatch)
        {
            return SearchOutputFormatting.WritePathIf(output, prefix, color, !hasSelectedMatch, nullPathTerminator, separators.LineTerminator);
        }

        if (searchMode == CliSearchMode.Count || searchMode == CliSearchMode.CountMatches)
        {
            return SearchOutputFormatting.WriteCount(output, prefix, color, count, includeZero, nullPathTerminator, separators.LineTerminator);
        }

        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        ulong emitted = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            ContextLineInfo line = lines[index];
            if (!line.SelectedMatch)
            {
                continue;
            }

            sink.MatchedLine(line.LineNumber, line.Start, matchColumn: 0, bytes.Slice(line.Start, line.Length));
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }
        }

        return hasSelectedMatch;
    }

    private static bool SearchMultilineContextBytes(
        ReadOnlySpan<byte> searchBytes,
        ReadOnlySpan<byte> outputBytes,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool invertMatch,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator)
    {
        if (invertMatch)
        {
            return SearchMultilineInvertedContextBytes(searchBytes, outputBytes, patterns, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall, maxCount, trim, beforeContext, afterContext, passthru, nullPathTerminator);
        }

        List<RegexMatch> matches = CollectMultilineMatches(searchBytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
        List<ContextLineInfo> lines = BuildMultilineContextLines(outputBytes, matches);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : IncludeMultilineContextLines(outputBytes, lines, matches, included, beforeContext, afterContext, maxCount);
        var lineSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int previousLineIndex = -1;
        bool wrote = false;
        ulong? renderedMatchLimit = passthru ? maxCount : null;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            if (!passthru && wrote && index > previousLineIndex + 1 && separators.ContextEnabled)
            {
                output.Write(separators.Context.Span);
                output.Write(separators.LineTerminator.Span);
            }

            bool wroteLine = WriteMultilineContextOutputLine(
                outputBytes,
                lines,
                index,
                matches,
                renderedMatchLimit,
                output,
                separators,
                prefix,
                lineLimit,
                color,
                lineNumber,
                column,
                byteOffset,
                trim,
                nullPathTerminator,
                vimgrep,
                onlyMatching,
                replacement,
                patterns,
                asciiCaseInsensitive,
                ref lineSink,
                out int consumedLineIndex);
            previousLineIndex = Math.Max(previousLineIndex, consumedLineIndex);
            if (wroteLine)
            {
                wrote = true;
            }

            index = Math.Max(index, consumedLineIndex);
        }

        return matched;
    }

    private static bool SearchMultilineInvertedContextBytes(
        ReadOnlySpan<byte> searchBytes,
        ReadOnlySpan<byte> outputBytes,
        IReadOnlyList<byte[]> patterns,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount,
        bool trim,
        ulong beforeContext,
        ulong afterContext,
        bool passthru,
        bool nullPathTerminator)
    {
        List<ContextLineInfo> lines = BuildMultilineInvertedLines(searchBytes, patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, multilineDotall);
        bool[] included = new bool[lines.Count];
        bool matched = passthru
            ? ContextSearchOperations.IncludePassthruLines(lines, included)
            : ContextSearchOperations.IncludeContextLines(lines, included, beforeContext, afterContext, maxCount);
        var lineSink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int previousLineIndex = -1;
        ulong passthruPrimaryMatches = 0;
        bool wrote = false;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!included[index])
            {
                continue;
            }

            ContextLineInfo line = lines[index];
            bool selectedMatch = line.SelectedMatch;
            if (passthru && selectedMatch && maxCount is ulong limit)
            {
                if (passthruPrimaryMatches >= limit)
                {
                    selectedMatch = false;
                }
                else
                {
                    passthruPrimaryMatches++;
                }
            }

            if (!passthru && wrote && index > previousLineIndex + 1 && separators.ContextEnabled)
            {
                output.Write(separators.Context.Span);
                output.Write(separators.LineTerminator.Span);
            }

            ReadOnlySpan<byte> lineBytes = outputBytes.Slice(line.Start, line.Length);
            if (selectedMatch)
            {
                lineSink.MatchedLine(line.LineNumber, line.Start, matchColumn: 0, lineBytes);
            }
            else
            {
                lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, lineBytes);
            }

            previousLineIndex = index;
            wrote = true;
        }

        return matched;
    }

    private static bool WriteMultilineContextOutputLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool vimgrep,
        bool onlyMatching,
        ReadOnlyMemory<byte>? replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        ref StandardSearchSink lineSink,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        bool selectedMatch = line.SelectedMatch &&
            MultilineLineHasRenderedMatch(bytes, line, matches, renderedMatchLimit);
        ReadOnlySpan<byte> outputLine = bytes.Slice(line.Start, line.Length);
        if (!selectedMatch)
        {
            lineSink.ContextLine(line.LineNumber, line.Start, line.ContextColumn, outputLine);
            return true;
        }

        if (replacement is ReadOnlyMemory<byte> replacementValue)
        {
            if (onlyMatching)
            {
                return WriteMultilineOnlyMatchingReplacementsForContextLine(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, asciiCaseInsensitive, out consumedLineIndex);
            }

            return TryWriteMultilineContextReplacementRecord(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementValue, patterns, asciiCaseInsensitive, out consumedLineIndex);
        }

        if (onlyMatching)
        {
            return WriteMultilineOnlyMatchesForContextLine(bytes, line, matches, renderedMatchLimit, output, separators, prefix, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
        }

        if (vimgrep)
        {
            return WriteMultilineVimgrepMatchesForContextLine(bytes, lines, lineIndex, matches, renderedMatchLimit, output, separators, prefix, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, out consumedLineIndex);
        }

        if (color.Enabled &&
            TryGetMultilineLineHighlights(bytes, line, matches, renderedMatchLimit, out List<int> highlightStarts, out List<int> highlightLengths))
        {
            lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, outputLine, highlightStarts, highlightLengths);
            return true;
        }

        lineSink.MatchedLine(line.LineNumber, line.Start, line.MatchColumn, outputLine);
        return true;
    }

    private static long CountMultilineMatches(ReadOnlySpan<byte> bytes, RegexAutomaton[] automata, bool lineRegexp, bool wordRegexp, ulong? maxCount)
    {
        long count = 0;
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            count++;
            if (maxCount is ulong limit && (ulong)count >= limit)
            {
                return count;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        return count;
    }

    private static void WriteSignatureArityMatchedLines(
        ReadOnlySpan<byte> bytes,
        SignatureArityAccelerator accelerator,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount,
        RegexMatch firstMatch)
    {
        if (color.Enabled)
        {
            WriteColoredSignatureArityMatchedLines(bytes, accelerator, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, maxCount, firstMatch);
            return;
        }

        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int lastWrittenLineStart = -1;
        int currentLineStart = 0;
        long currentLineNumber = 1;
        ulong emitted = 0;
        RegexMatch match = firstMatch;
        bool haveMatch = true;
        while (haveMatch)
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            AdvanceLineNumber(bytes, firstLineStart, ref currentLineStart, ref currentLineNumber);
            long outputLineNumber = currentLineNumber;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart > lastWrittenLineStart)
                {
                    long matchColumn = GetMultilineMatchColumn(match, lineStart);
                    sink.MatchedLine(outputLineNumber, lineStart, matchColumn, bytes[lineStart..lineEnd]);
                    lastWrittenLineStart = lineStart;
                    emitted++;
                    if (maxCount is ulong limit && emitted >= limit)
                    {
                        return;
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
                outputLineNumber++;
            }

            currentLineStart = Math.Max(currentLineStart, GetNextLineStart(GetLineEnd(bytes, lastLineStart), bytes.Length));
            currentLineNumber = Math.Max(currentLineNumber, outputLineNumber);
            offset = match.Start + match.Length;
            haveMatch = accelerator.TryFindNext(bytes, offset, out match);
        }
    }

    private static void WriteColoredSignatureArityMatchedLines(
        ReadOnlySpan<byte> bytes,
        SignatureArityAccelerator accelerator,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ulong? maxCount,
        RegexMatch firstMatch)
    {
        var sink = new ColoredSearchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int lastWrittenLineStart = -1;
        int currentLineStart = 0;
        long currentLineNumber = 1;
        ulong emitted = 0;
        RegexMatch match = firstMatch;
        bool haveMatch = true;
        while (haveMatch)
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            AdvanceLineNumber(bytes, firstLineStart, ref currentLineStart, ref currentLineNumber);
            long outputLineNumber = currentLineNumber;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                bool newLine = lineStart > lastWrittenLineStart;
                if (newLine || lineStart == lastWrittenLineStart)
                {
                    int matchByteOffset = Math.Max(match.Start, lineStart);
                    int matchEnd = Math.Min(match.Start + match.Length, GetLineContentEnd(bytes, lineStart));
                    if (matchEnd < matchByteOffset)
                    {
                        matchEnd = matchByteOffset;
                    }

                    ReadOnlySpan<byte> lineBytes = bytes[lineStart..lineEnd];
                    ReadOnlySpan<byte> matchedBytes = bytes[matchByteOffset..matchEnd];
                    long matchColumn = GetMultilineMatchColumn(match, lineStart);
                    sink.MatchedLine(outputLineNumber, lineStart, matchByteOffset, matchColumn, lineBytes, matchedBytes);
                    if (newLine)
                    {
                        lastWrittenLineStart = lineStart;
                        emitted++;
                        if (maxCount is ulong limit && emitted >= limit)
                        {
                            sink.Flush();
                            return;
                        }
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
                outputLineNumber++;
            }

            currentLineStart = Math.Max(currentLineStart, GetNextLineStart(GetLineEnd(bytes, lastLineStart), bytes.Length));
            currentLineNumber = Math.Max(currentLineNumber, outputLineNumber);
            offset = match.Start + match.Length;
            haveMatch = accelerator.TryFindNext(bytes, offset, out match);
        }

        sink.Flush();
    }

    private static long GetMultilineMatchColumn(RegexMatch match, int lineStart)
    {
        return match.Start >= lineStart ? match.Start - lineStart + 1L : 1;
    }

    private static RegexAutomaton[] CompileMultilineAutomata(IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool multilineDotall)
    {
        var automata = new RegexAutomaton[patterns.Count];
        for (int index = 0; index < patterns.Count; index++)
        {
            automata[index] = RegexAutomaton.Compile(patterns[index], asciiCaseInsensitive, multiLine: true, dotMatchesNewline: multilineDotall);
        }

        return automata;
    }

    private static bool TryCreateSignatureArityAccelerator(
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        out SignatureArityAccelerator? accelerator)
    {
        accelerator = null;
        if (patterns.Count != 1 ||
            asciiCaseInsensitive ||
            lineRegexp ||
            wordRegexp)
        {
            return false;
        }

        RegexSyntaxTree tree = RegexSyntaxParser.Parse(patterns[0]);
        if (UnwrapFlaglessGroup(tree.Root) is not RegexSequenceNode sequence ||
            sequence.Nodes.Count != 6 ||
            !TryReadPrefixAlternatives(sequence.Nodes[0], out byte[][] prefixes) ||
            !TryReadNegatedLiteralClassStar(sequence.Nodes[1], out bool[] preOpenTerminators) ||
            !TryReadLiteralByte(sequence.Nodes[2], (byte)'(') ||
            !TryReadNegatedLiteralClassStar(sequence.Nodes[3], out bool[] closeTerminators) ||
            !IsOnlyExcludedByte(closeTerminators, (byte)')') ||
            !TryReadRepeatedCommaTail(sequence.Nodes[4], closeTerminators, out int requiredCommas) ||
            !TryReadLiteralByte(sequence.Nodes[5], (byte)')'))
        {
            return false;
        }

        accelerator = new SignatureArityAccelerator(prefixes, preOpenTerminators, requiredCommas);
        return true;
    }

    private static RegexSyntaxNode UnwrapFlaglessGroup(RegexSyntaxNode node)
    {
        while (node is RegexGroupNode { EnabledFlags.Length: 0, DisabledFlags.Length: 0 } group)
        {
            node = group.Child;
        }

        return node;
    }

    private static bool TryReadPrefixAlternatives(RegexSyntaxNode node, out byte[][] prefixes)
    {
        node = UnwrapFlaglessGroup(node);
        if (node is RegexAlternationNode alternation)
        {
            byte[][] alternatives = new byte[alternation.Alternatives.Count][];
            for (int index = 0; index < alternation.Alternatives.Count; index++)
            {
                if (!TryReadLiteralSequence(alternation.Alternatives[index], out byte[] literal) ||
                    literal.Length == 0)
                {
                    prefixes = [];
                    return false;
                }

                alternatives[index] = literal;
            }

            prefixes = alternatives;
            return prefixes.Length > 0;
        }

        if (TryReadLiteralSequence(node, out byte[] prefix) &&
            prefix.Length > 0)
        {
            prefixes = [prefix];
            return true;
        }

        prefixes = [];
        return false;
    }

    private static bool TryReadLiteralSequence(RegexSyntaxNode node, out byte[] literal)
    {
        node = UnwrapFlaglessGroup(node);
        if (node is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom)
        {
            literal = atom.Value.ToArray();
            return true;
        }

        if (node is not RegexSequenceNode sequence)
        {
            literal = [];
            return false;
        }

        var bytes = new List<byte>();
        for (int index = 0; index < sequence.Nodes.Count; index++)
        {
            RegexSyntaxNode child = UnwrapFlaglessGroup(sequence.Nodes[index]);
            if (child is not RegexAtomNode { Kind: RegexSyntaxKind.Literal } childAtom)
            {
                literal = [];
                return false;
            }

            bytes.AddRange(childAtom.Value.ToArray());
        }

        literal = bytes.ToArray();
        return true;
    }

    private static bool TryReadLiteralByte(RegexSyntaxNode node, byte expected)
    {
        return UnwrapFlaglessGroup(node) is RegexAtomNode { Kind: RegexSyntaxKind.Literal } atom &&
            atom.Value.Span.Length == 1 &&
            atom.Value.Span[0] == expected;
    }

    private static bool TryReadNegatedLiteralClassStar(RegexSyntaxNode node, out bool[] excluded)
    {
        excluded = new bool[256];
        if (UnwrapFlaglessGroup(node) is not RegexRepetitionNode { Minimum: 0, Maximum: null } repetition ||
            UnwrapFlaglessGroup(repetition.Child) is not RegexAtomNode { Kind: RegexSyntaxKind.CharacterClass } atom)
        {
            return false;
        }

        return TryReadNegatedLiteralClass(atom.Value.Span, excluded);
    }

    private static bool TryReadNegatedLiteralClass(ReadOnlySpan<byte> expression, bool[] excluded)
    {
        if (expression.IsEmpty ||
            expression[0] != (byte)'^')
        {
            return false;
        }

        for (int index = 1; index < expression.Length; index++)
        {
            byte value = expression[index];
            if (value == (byte)'\\')
            {
                if (++index >= expression.Length)
                {
                    return false;
                }

                value = expression[index] switch
                {
                    (byte)'t' => (byte)'\t',
                    (byte)'r' => (byte)'\r',
                    (byte)'n' => (byte)'\n',
                    (byte)'f' => (byte)'\f',
                    byte escaped => escaped,
                };
            }

            excluded[value] = true;
        }

        return true;
    }

    private static bool TryReadRepeatedCommaTail(RegexSyntaxNode node, bool[] closeTerminators, out int requiredCommas)
    {
        requiredCommas = 0;
        if (UnwrapFlaglessGroup(node) is not RegexRepetitionNode { Minimum: > 0, Maximum: null } repetition ||
            UnwrapFlaglessGroup(repetition.Child) is not RegexSequenceNode sequence ||
            sequence.Nodes.Count != 2 ||
            !TryReadLiteralByte(sequence.Nodes[0], (byte)',') ||
            !TryReadNegatedLiteralClassStar(sequence.Nodes[1], out bool[] repeatedTerminators) ||
            !ExcludedSetsEqual(closeTerminators, repeatedTerminators))
        {
            return false;
        }

        requiredCommas = repetition.Minimum;
        return true;
    }

    private static bool IsOnlyExcludedByte(bool[] excluded, byte value)
    {
        for (int index = 0; index < excluded.Length; index++)
        {
            if (excluded[index] != (index == value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExcludedSetsEqual(bool[] left, bool[] right)
    {
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryFindNextMultilineMatch(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ref int offset,
        ref int suppressedEmptyStart,
        out RegexMatch match)
    {
        RegexAutomaton[] automata = CompileMultilineAutomata(patterns, asciiCaseInsensitive, multilineDotall);
        return TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out match);
    }

    internal static bool TryCollectSignatureArityMatches(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        out List<RegexMatch> matches)
    {
        matches = [];
        if (!TryCreateSignatureArityAccelerator(patterns, asciiCaseInsensitive, lineRegexp, wordRegexp, out SignatureArityAccelerator? accelerator))
        {
            return false;
        }

        int offset = 0;
        while (accelerator!.TryFindNext(bytes, offset, out RegexMatch match))
        {
            matches.Add(match);
            offset = match.Start + match.Length;
        }

        return true;
    }

    private static bool TryFindNextMultilineMatch(
        ReadOnlySpan<byte> bytes,
        RegexAutomaton[] automata,
        bool lineRegexp,
        bool wordRegexp,
        ref int offset,
        ref int suppressedEmptyStart,
        out RegexMatch match)
    {
        while (offset <= bytes.Length && TryFindMultilineMatch(bytes, automata, lineRegexp, wordRegexp, offset, out match))
        {
            if (IsTrailingEmptyLineMatch(bytes, match))
            {
                offset = bytes.Length + 1;
                break;
            }

            var matcherMatch = new MatcherMatch(match.Start, match.Length);
            if (!MatchIterator.IsSuppressedEmpty(matcherMatch, suppressedEmptyStart))
            {
                return true;
            }

            offset = MatchIterator.AdvanceAfterSuppressedEmpty(matcherMatch, bytes.Length);
            suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        }

        match = default;
        return false;
    }

    private static bool IsTrailingEmptyLineMatch(ReadOnlySpan<byte> bytes, RegexMatch match)
    {
        return match.Length == 0 &&
            match.Start == bytes.Length &&
            (bytes.IsEmpty || bytes[^1] == (byte)'\n');
    }

    private static bool IsEofEmptyMatch(ReadOnlySpan<byte> bytes, RegexMatch match)
    {
        return match.Length == 0 &&
            match.Start == bytes.Length &&
            !bytes.IsEmpty &&
            bytes[^1] != (byte)'\n';
    }

    internal static int AdvanceAfterReportedMultilineMatch(RegexMatch match, int haystackLength, ref int suppressedEmptyStart)
    {
        return MatchIterator.AdvanceAfterReported(new MatcherMatch(match.Start, match.Length), haystackLength, ref suppressedEmptyStart);
    }

    private static void WriteMultilineMatchedLines(
        ReadOnlySpan<byte> bytes,
        RegexAutomaton[] automata,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount)
    {
        if (color.Enabled)
        {
            WriteColoredMultilineMatchedLines(bytes, automata, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, lineRegexp, wordRegexp, maxCount);
            return;
        }

        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        int lastWrittenLineStart = -1;
        int currentLineStart = 0;
        long currentLineNumber = 1;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            AdvanceLineNumber(bytes, firstLineStart, ref currentLineStart, ref currentLineNumber);
            long outputLineNumber = currentLineNumber;
            long matchColumn = match.Start - firstLineStart + 1L;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart > lastWrittenLineStart)
                {
                    sink.MatchedLine(outputLineNumber, lineStart, matchColumn, bytes[lineStart..lineEnd]);
                    lastWrittenLineStart = lineStart;
                    emitted++;
                    if (maxCount is ulong limit && emitted >= limit)
                    {
                        return;
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
                outputLineNumber++;
            }

            currentLineStart = Math.Max(currentLineStart, GetNextLineStart(GetLineEnd(bytes, lastLineStart), bytes.Length));
            currentLineNumber = Math.Max(currentLineNumber, outputLineNumber);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static void WriteColoredMultilineMatchedLines(
        ReadOnlySpan<byte> bytes,
        RegexAutomaton[] automata,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount)
    {
        var sink = new ColoredSearchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        int lastWrittenLineStart = -1;
        int currentLineStart = 0;
        long currentLineNumber = 1;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            AdvanceLineNumber(bytes, firstLineStart, ref currentLineStart, ref currentLineNumber);
            long outputLineNumber = currentLineNumber;
            for (int lineStart = firstLineStart; lineStart <= lastLineStart;)
            {
                int lineEnd = GetLineEnd(bytes, lineStart);
                if (lineStart >= lastWrittenLineStart)
                {
                    int matchByteOffset = Math.Max(match.Start, lineStart);
                    int matchEnd = Math.Min(match.Start + match.Length, GetLineContentEnd(bytes, lineStart));
                    ReadOnlySpan<byte> lineBytes = bytes[lineStart..lineEnd];
                    ReadOnlySpan<byte> matchedBytes = bytes[matchByteOffset..matchEnd];
                    long matchColumn = matchByteOffset - lineStart + 1L;
                    sink.MatchedLine(outputLineNumber, lineStart, matchByteOffset, matchColumn, lineBytes, matchedBytes);
                    if (lineStart > lastWrittenLineStart)
                    {
                        lastWrittenLineStart = lineStart;
                        emitted++;
                        if (maxCount is ulong limit && emitted >= limit)
                        {
                            sink.Flush();
                            return;
                        }
                    }
                }

                lineStart = GetNextLineStart(lineEnd, bytes.Length);
                outputLineNumber++;
            }

            currentLineStart = Math.Max(currentLineStart, GetNextLineStart(GetLineEnd(bytes, lastLineStart), bytes.Length));
            currentLineNumber = Math.Max(currentLineNumber, outputLineNumber);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        sink.Flush();
    }

    private static void AdvanceLineNumber(ReadOnlySpan<byte> bytes, int targetLineStart, ref int currentLineStart, ref long currentLineNumber)
    {
        if (targetLineStart < currentLineStart)
        {
            currentLineNumber = GetLineNumber(bytes, targetLineStart);
            currentLineStart = targetLineStart;
            return;
        }

        while (currentLineStart < targetLineStart)
        {
            int lineEnd = GetLineEnd(bytes, currentLineStart);
            currentLineStart = GetNextLineStart(lineEnd, bytes.Length);
            currentLineNumber++;
        }
    }

    private static void WriteMultilineReplacedLines(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        RegexAutomaton[] automata,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        int groupStart = -1;
        int groupEnd = -1;
        List<RegexMatch> groupMatches = [];
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            GetMultilineReplacementRange(bytes, match, out int rangeStart, out int rangeEnd);
            if (groupStart >= 0 && rangeStart >= groupEnd)
            {
                WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns, asciiCaseInsensitive);
                groupMatches.Clear();
                groupStart = -1;
                groupEnd = -1;
            }

            if (groupStart < 0)
            {
                groupStart = rangeStart;
                groupEnd = rangeEnd;
            }
            else if (rangeEnd > groupEnd)
            {
                groupEnd = rangeEnd;
            }

            groupMatches.Add(match);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        if (groupStart >= 0)
        {
            WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns, asciiCaseInsensitive);
        }
    }

    private static void WriteMultilineOnlyMatchingReplacements(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<byte[]> patterns,
        RegexAutomaton[] automata,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        bool asciiCaseInsensitive,
        bool lineRegexp,
        bool wordRegexp,
        bool multilineDotall,
        ulong? maxCount)
    {
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            byte[] body = ReplacementFormatter.Expand(replacement.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive);
            int lineStart = GetLineStart(bytes, match.Start);
            WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                break;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static void WriteMultilineReplacementRecord(
        ReadOnlySpan<byte> bytes,
        int recordStart,
        int recordEnd,
        List<RegexMatch> matches,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive)
    {
        List<int> starts = [];
        List<int> lengths = [];
        for (int index = 0; index < matches.Count; index++)
        {
            starts.Add(matches[index].Start - recordStart);
            lengths.Add(matches[index].Length);
        }

        List<long> replacementColumns = [];
        List<int> replacementLengths = [];
        byte[] body = ReplacementFormatter.ReplaceLine(bytes[recordStart..recordEnd], starts, lengths, replacement.Span, patterns, asciiCaseInsensitive, replacementColumns, replacementLengths);
        int lineStart = GetLineStart(bytes, recordStart);
        WriteMultilineReplacementBody(body, recordStart, GetLineNumber(bytes, lineStart), replacementColumns.Count > 0 ? replacementColumns[0] : 1, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacementColumns, replacementLengths);
    }

    private static void WriteMultilineReplacementBody(
        ReadOnlySpan<byte> body,
        long byteOffsetStart,
        long lineNumberStart,
        long firstColumn,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        IReadOnlyList<long>? replacementColumns = null,
        IReadOnlyList<int>? replacementLengths = null)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        if (body.IsEmpty)
        {
            sink.MatchedLine(lineNumberStart, byteOffsetStart, firstColumn, []);
            return;
        }

        int outputLineStart = 0;
        long outputLineNumber = lineNumberStart;
        long outputByteOffset = byteOffsetStart;
        while (outputLineStart <= body.Length)
        {
            int outputLineEnd = GetLineEnd(body, outputLineStart);
            long outputColumn = outputLineStart == 0 ? firstColumn : 1;
            if (replacementColumns is not null && replacementLengths is not null)
            {
                List<int> lineStarts = [];
                List<int> lineLengths = [];
                AddLineReplacementSpans(outputLineStart, outputLineEnd, replacementColumns, replacementLengths, lineStarts, lineLengths);
                sink.MatchedLine(outputLineNumber, outputByteOffset, outputColumn, body[outputLineStart..outputLineEnd], lineStarts, lineLengths);
            }
            else
            {
                sink.MatchedLine(outputLineNumber, outputByteOffset, outputColumn, body[outputLineStart..outputLineEnd]);
            }

            if (outputLineEnd >= body.Length)
            {
                break;
            }

            outputByteOffset += outputLineEnd - outputLineStart;
            outputLineNumber++;
            outputLineStart = outputLineEnd;
        }
    }

    private static void AddLineReplacementSpans(
        int lineStart,
        int lineEnd,
        IReadOnlyList<long> replacementColumns,
        IReadOnlyList<int> replacementLengths,
        List<int> lineStarts,
        List<int> lineLengths)
    {
        for (int index = 0; index < replacementColumns.Count; index++)
        {
            int replacementStart = checked((int)replacementColumns[index] - 1);
            int replacementEnd = replacementStart + replacementLengths[index];
            if (replacementEnd <= lineStart || replacementStart >= lineEnd)
            {
                continue;
            }

            int start = Math.Max(replacementStart, lineStart);
            int end = Math.Min(replacementEnd, lineEnd);
            lineStarts.Add(start - lineStart);
            lineLengths.Add(end - start);
        }
    }

    private static void GetMultilineReplacementRange(ReadOnlySpan<byte> bytes, RegexMatch match, out int start, out int end)
    {
        start = GetLineStart(bytes, match.Start);
        int endAnchor = match.Length == 0 ? match.Start : match.Start + match.Length;
        int lastLineStart = GetLineStart(bytes, endAnchor);
        end = GetLineEnd(bytes, lastLineStart);
    }

    private static void WriteMultilineOnlyMatches(
        ReadOnlySpan<byte> bytes,
        RegexAutomaton[] automata,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount)
    {
        var sink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int firstLineStart = GetLineStart(bytes, match.Start);
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            int matchEnd = match.Start + match.Length;
            if (IsEofEmptyMatch(bytes, match))
            {
                int lineEnd = GetLineEnd(bytes, firstLineStart);
                sink.Matched(firstLineNumber, firstLineStart, 1, bytes[firstLineStart..lineEnd]);
            }
            else if (match.Length == 0)
            {
                sink.Matched(firstLineNumber, match.Start, matchColumn, []);
            }
            else
            {
                for (int lineStart = firstLineStart; lineStart < matchEnd;)
                {
                    int lineEnd = GetLineEnd(bytes, lineStart);
                    int segmentStart = Math.Max(lineStart, match.Start);
                    int segmentEnd = Math.Min(lineEnd, matchEnd);
                    if (segmentStart < segmentEnd)
                    {
                        sink.Matched(firstLineNumber + CountLineFeeds(bytes[firstLineStart..lineStart]), match.Start, matchColumn, bytes[segmentStart..segmentEnd]);
                    }

                    lineStart = GetNextLineStart(lineEnd, bytes.Length);
                }
            }

            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static void WriteMultilineVimgrepMatches(
        ReadOnlySpan<byte> bytes,
        RegexAutomaton[] automata,
        RawByteWriter output,
        OutputPath? prefix,
        OutputSeparators separators,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool trim,
        bool nullPathTerminator,
        bool lineRegexp,
        bool wordRegexp,
        ulong? maxCount)
    {
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber: true, column: true, byteOffset: false, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        ulong emitted = 0;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            int lineStart = GetLineStart(bytes, match.Start);
            int lineEnd = GetLineEnd(bytes, lineStart);
            long lineNumber = GetLineNumber(bytes, lineStart);
            long matchColumn = match.Start - lineStart + 1L;
            sink.MatchedLine(lineNumber, lineStart, matchColumn, bytes[lineStart..lineEnd], match.Start - lineStart, match.Length);
            emitted++;
            if (maxCount is ulong limit && emitted >= limit)
            {
                return;
            }

            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }
    }

    private static bool TryFindMultilineMatch(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall, int startAt, out RegexMatch match)
    {
        RegexAutomaton[] automata = CompileMultilineAutomata(patterns, asciiCaseInsensitive, multilineDotall);
        return TryFindMultilineMatch(bytes, automata, lineRegexp, wordRegexp, startAt, out match);
    }

    private static bool TryFindMultilineMatch(ReadOnlySpan<byte> bytes, RegexAutomaton[] automata, bool lineRegexp, bool wordRegexp, int startAt, out RegexMatch match)
    {
        match = default;
        bool found = false;
        for (int index = 0; index < automata.Length; index++)
        {
            RegexAutomaton automaton = automata[index];
            int candidateStartAt = startAt;
            RegexMatch? candidate = null;
            while (candidateStartAt <= bytes.Length)
            {
                RegexMatch? next = automaton.Find(bytes, candidateStartAt);
                if (!next.HasValue)
                {
                    break;
                }

                int end = next.Value.Start + next.Value.Length;
                if ((!lineRegexp || IsLineMatch(bytes, next.Value.Start, end)) &&
                    (!wordRegexp || IsWordMatch(bytes, next.Value.Start, end)))
                {
                    candidate = next.Value;
                    break;
                }

                candidateStartAt = MatchIterator.AdvanceAfter(new MatcherMatch(next.Value.Start, next.Value.Length), bytes.Length);
            }

            if (candidate.HasValue &&
                (!found ||
                candidate.Value.Start < match.Start ||
                (candidate.Value.Start == match.Start && candidate.Value.Length > match.Length)))
            {
                match = candidate.Value;
                found = true;
            }
        }

        return found;
    }

    internal static List<RegexMatch> CollectMultilineMatches(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall)
    {
        List<RegexMatch> matches = [];
        RegexAutomaton[] automata = CompileMultilineAutomata(patterns, asciiCaseInsensitive, multilineDotall);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            matches.Add(match);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        return matches;
    }

    internal static bool IncludeMultilineContextLines(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        List<RegexMatch> matches,
        bool[] included,
        ulong beforeContext,
        ulong afterContext,
        ulong? maxCount)
    {
        bool matched = false;
        ulong primaryMatches = 0;
        for (int index = 0; index < matches.Count; index++)
        {
            if (maxCount is ulong limit && primaryMatches >= limit)
            {
                break;
            }

            int firstLineStart = GetLineStart(bytes, matches[index].Start);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(matches[index]));
            int firstLineIndex = GetMultilineLineIndex(lines, firstLineStart);
            int lastLineIndex = GetMultilineLineIndex(lines, lastLineStart);
            if (firstLineIndex < 0 || lastLineIndex < 0)
            {
                continue;
            }

            matched = true;
            primaryMatches++;
            IncludeMultilineContextRange(included, firstLineIndex, lastLineIndex, beforeContext, afterContext);
        }

        return matched;
    }

    private static void IncludeMultilineContextRange(bool[] included, int firstLineIndex, int lastLineIndex, ulong beforeContext, ulong afterContext)
    {
        int startIndex = beforeContext >= (ulong)firstLineIndex
            ? 0
            : firstLineIndex - (int)beforeContext;
        ulong requestedEnd = (ulong)lastLineIndex + afterContext;
        int endIndex = requestedEnd >= (ulong)included.Length
            ? included.Length - 1
            : (int)requestedEnd;
        for (int index = startIndex; index <= endIndex; index++)
        {
            included[index] = true;
        }
    }

    internal static List<ContextLineInfo> BuildMultilineContextLines(ReadOnlySpan<byte> bytes, List<RegexMatch> matches, bool stopOnNonmatch = false)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes[lineStart..], nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        long[] matchColumns = new long[physicalLines.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            MarkMultilineContextMatch(bytes, physicalLines, matchedLines, matchColumns, matches[index]);
        }

        var lines = new List<ContextLineInfo>(physicalLines.Count);
        bool hasSelectedMatch = false;
        for (int index = 0; index < physicalLines.Count; index++)
        {
            ContextLineInfo line = physicalLines[index];
            bool selected = matchedLines[index];
            lines.Add(new ContextLineInfo(line.Start, line.Length, line.LineNumber, selected, matchColumns[index], selected, matchColumns[index]));
            if (stopOnNonmatch && hasSelectedMatch && !selected)
            {
                break;
            }

            hasSelectedMatch |= selected;
        }

        return lines;
    }

    private static List<ContextLineInfo> BuildMultilineContextLines(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes[lineStart..], nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        long[] matchColumns = new long[physicalLines.Count];
        RegexAutomaton[] automata = CompileMultilineAutomata(patterns, asciiCaseInsensitive, multilineDotall);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            MarkMultilineContextMatch(bytes, physicalLines, matchedLines, matchColumns, match);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        var lines = new List<ContextLineInfo>(physicalLines.Count);
        for (int index = 0; index < physicalLines.Count; index++)
        {
            ContextLineInfo line = physicalLines[index];
            lines.Add(new ContextLineInfo(line.Start, line.Length, line.LineNumber, matchedLines[index], matchColumns[index], matchedLines[index], contextColumn: 0));
        }

        return lines;
    }

    internal static List<ContextLineInfo> BuildMultilineInvertedLines(ReadOnlySpan<byte> bytes, IReadOnlyList<byte[]> patterns, bool asciiCaseInsensitive, bool lineRegexp, bool wordRegexp, bool multilineDotall)
    {
        var physicalLines = new List<ContextLineInfo>();
        int lineStart = 0;
        long lineNumber = 1;
        while (lineStart < bytes.Length)
        {
            int lineLength = ContextSearchOperations.GetLineLength(bytes[lineStart..], nullData: false);
            physicalLines.Add(new ContextLineInfo(lineStart, lineLength, lineNumber, selectedMatch: false, matchColumn: 0, originalMatch: false, contextColumn: 0));
            lineStart += lineLength;
            lineNumber++;
        }

        bool[] matchedLines = new bool[physicalLines.Count];
        RegexAutomaton[] automata = CompileMultilineAutomata(patterns, asciiCaseInsensitive, multilineDotall);
        int offset = 0;
        int suppressedEmptyStart = MatchIterator.NoSuppressedEmptyStart;
        while (TryFindNextMultilineMatch(bytes, automata, lineRegexp, wordRegexp, ref offset, ref suppressedEmptyStart, out RegexMatch match))
        {
            MarkMultilineMatchedLines(bytes, physicalLines, matchedLines, match);
            offset = AdvanceAfterReportedMultilineMatch(match, bytes.Length, ref suppressedEmptyStart);
        }

        var lines = new List<ContextLineInfo>(physicalLines.Count);
        for (int index = 0; index < physicalLines.Count; index++)
        {
            ContextLineInfo line = physicalLines[index];
            bool originalMatch = matchedLines[index];
            lines.Add(new ContextLineInfo(line.Start, line.Length, line.LineNumber, selectedMatch: !originalMatch, matchColumn: 0, originalMatch, contextColumn: 0));
        }

        return lines;
    }

    private static void MarkMultilineContextMatch(ReadOnlySpan<byte> bytes, List<ContextLineInfo> lines, bool[] matchedLines, long[] matchColumns, RegexMatch match)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
        long matchColumn = match.Start - firstLineStart + 1L;
        for (int index = 0; index < lines.Count; index++)
        {
            int lineStart = lines[index].Start;
            if (lineStart < firstLineStart)
            {
                continue;
            }

            if (lineStart > lastLineStart)
            {
                break;
            }

            matchedLines[index] = true;
            if (matchColumns[index] == 0)
            {
                matchColumns[index] = matchColumn;
            }
        }
    }

    private static void MarkMultilineMatchedLines(ReadOnlySpan<byte> bytes, List<ContextLineInfo> lines, bool[] matchedLines, RegexMatch match)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
        for (int index = 0; index < lines.Count; index++)
        {
            int lineStart = lines[index].Start;
            if (lineStart < firstLineStart)
            {
                continue;
            }

            if (lineStart > lastLineStart)
            {
                break;
            }

            matchedLines[index] = true;
        }
    }

    internal static int GetMultilineLineIndex(List<ContextLineInfo> lines, int lineStart)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (lines[index].Start == lineStart)
            {
                return index;
            }
        }

        return -1;
    }

    internal static bool MultilineLineHasRenderedMatch(ReadOnlySpan<byte> bytes, ContextLineInfo line, List<RegexMatch> matches, ulong? renderedMatchLimit)
    {
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            if (MultilineMatchTouchesLine(bytes, matches[index], line))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MultilineMatchTouchesLine(ReadOnlySpan<byte> bytes, RegexMatch match, ContextLineInfo line)
    {
        int firstLineStart = GetLineStart(bytes, match.Start);
        int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
        return line.Start >= firstLineStart && line.Start <= lastLineStart;
    }

    private static bool TryGetMultilineLineHighlights(
        ReadOnlySpan<byte> bytes,
        ContextLineInfo line,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        out List<int> starts,
        out List<int> lengths)
    {
        starts = [];
        lengths = [];
        int lineContentEnd = GetLineContentEnd(bytes, line.Start);
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            if (!MultilineMatchTouchesLine(bytes, match, line))
            {
                continue;
            }

            int start = Math.Max(match.Start, line.Start);
            int end = Math.Min(match.Start + match.Length, lineContentEnd);
            if (end <= start)
            {
                continue;
            }

            starts.Add(start - line.Start);
            lengths.Add(end - start);
        }

        return starts.Count > 0;
    }

    internal static bool IsMultilineContextMatchRendered(int matchIndex, ulong? renderedMatchLimit)
    {
        return renderedMatchLimit is not ulong limit || (ulong)matchIndex < limit;
    }

    private static bool WriteMultilineOnlyMatchesForContextLine(
        ReadOnlySpan<byte> bytes,
        ContextLineInfo line,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator)
    {
        var sink = new StandardMatchSink(output, prefix, separators.FieldMatch, lineNumber, column, byteOffset, trim, nullPathTerminator: nullPathTerminator, color: color, lineTerminator: separators.LineTerminator);
        bool wrote = false;
        int lineEnd = line.Start + line.Length;
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            if (!MultilineMatchTouchesLine(bytes, match, line))
            {
                continue;
            }

            int firstLineStart = GetLineStart(bytes, match.Start);
            long firstLineNumber = GetLineNumber(bytes, firstLineStart);
            long matchColumn = match.Start - firstLineStart + 1L;
            int matchEnd = match.Start + match.Length;
            if (IsEofEmptyMatch(bytes, match))
            {
                sink.Matched(firstLineNumber, firstLineStart, 1, bytes[firstLineStart..lineEnd]);
                wrote = true;
            }
            else if (match.Length == 0)
            {
                sink.Matched(firstLineNumber, match.Start, matchColumn, []);
                wrote = true;
            }
            else
            {
                int segmentStart = Math.Max(line.Start, match.Start);
                int segmentEnd = Math.Min(lineEnd, matchEnd);
                if (segmentStart < segmentEnd)
                {
                    sink.Matched(
                        firstLineNumber + CountLineFeeds(bytes[firstLineStart..line.Start]),
                        match.Start,
                        matchColumn,
                        bytes[segmentStart..segmentEnd]);
                    wrote = true;
                }
            }
        }

        return wrote;
    }

    private static bool WriteMultilineOnlyMatchingReplacementsForContextLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        bool wrote = false;
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            if (GetLineStart(bytes, match.Start) != line.Start)
            {
                continue;
            }

            byte[] body = ReplacementFormatter.Expand(replacement.Span, bytes.Slice(match.Start, match.Length), patterns, asciiCaseInsensitive);
            int lineStart = GetLineStart(bytes, match.Start);
            WriteMultilineReplacementBody(body, match.Start, GetLineNumber(bytes, lineStart), match.Start - lineStart + 1L, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            consumedLineIndex = Math.Max(consumedLineIndex, GetMultilineLineIndex(lines, lastLineStart));
            wrote = true;
        }

        return wrote;
    }

    private static bool TryWriteMultilineContextReplacementRecord(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        ReadOnlyMemory<byte> replacement,
        IReadOnlyList<byte[]> patterns,
        bool asciiCaseInsensitive,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        int groupStart = -1;
        int groupEnd = -1;
        List<RegexMatch> groupMatches = [];
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            GetMultilineReplacementRange(bytes, match, out int rangeStart, out int rangeEnd);
            if (groupStart < 0)
            {
                if (rangeStart != line.Start)
                {
                    continue;
                }

                groupStart = rangeStart;
                groupEnd = rangeEnd;
                groupMatches.Add(match);
                continue;
            }

            if (rangeStart >= groupEnd)
            {
                break;
            }

            if (rangeEnd > groupEnd)
            {
                groupEnd = rangeEnd;
            }

            groupMatches.Add(match);
        }

        if (groupStart < 0)
        {
            return false;
        }

        WriteMultilineReplacementRecord(bytes, groupStart, groupEnd, groupMatches, output, prefix, separators, lineLimit, color, lineNumber, column, byteOffset, trim, nullPathTerminator, replacement, patterns, asciiCaseInsensitive);
        int lastLineStart = GetLineStart(bytes, groupEnd > groupStart ? groupEnd - 1 : groupEnd);
        consumedLineIndex = Math.Max(consumedLineIndex, GetMultilineLineIndex(lines, lastLineStart));
        return true;
    }

    private static bool WriteMultilineVimgrepMatchesForContextLine(
        ReadOnlySpan<byte> bytes,
        List<ContextLineInfo> lines,
        int lineIndex,
        List<RegexMatch> matches,
        ulong? renderedMatchLimit,
        RawByteWriter output,
        OutputSeparators separators,
        OutputPath? prefix,
        OutputLineLimit lineLimit,
        OutputColor color,
        bool lineNumber,
        bool column,
        bool byteOffset,
        bool trim,
        bool nullPathTerminator,
        out int consumedLineIndex)
    {
        ContextLineInfo line = lines[lineIndex];
        consumedLineIndex = lineIndex;
        var sink = new StandardSearchSink(output, prefix, separators.FieldMatch, separators.FieldContext, lineNumber, column, byteOffset, trim, nullPathTerminator, lineLimit, color, separators.LineTerminator);
        bool wrote = false;
        for (int index = 0; index < matches.Count; index++)
        {
            if (!IsMultilineContextMatchRendered(index, renderedMatchLimit))
            {
                break;
            }

            RegexMatch match = matches[index];
            if (GetLineStart(bytes, match.Start) != line.Start)
            {
                continue;
            }

            int lineEnd = GetLineEnd(bytes, line.Start);
            sink.MatchedLine(line.LineNumber, line.Start, match.Start - line.Start + 1L, bytes[line.Start..lineEnd], match.Start - line.Start, match.Length);
            int lastLineStart = GetLineStart(bytes, GetInclusiveMatchEnd(match));
            consumedLineIndex = Math.Max(consumedLineIndex, GetMultilineLineIndex(lines, lastLineStart));
            wrote = true;
        }

        return wrote;
    }

    private static long CountSelectedMultilineLines(List<ContextLineInfo> lines, ulong? maxCount)
    {
        long count = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            if (!lines[index].SelectedMatch)
            {
                continue;
            }

            count++;
            if (maxCount is ulong limit && (ulong)count >= limit)
            {
                return count;
            }
        }

        return count;
    }

    private static bool IsLineMatch(ReadOnlySpan<byte> bytes, int start, int end)
    {
        int firstLineStart = GetLineStart(bytes, start);
        if (start != firstLineStart)
        {
            return false;
        }

        int lastLineStart = GetLineStart(bytes, end > start ? end - 1 : start);
        return end == GetLineContentEnd(bytes, lastLineStart);
    }

    private static bool IsWordMatch(ReadOnlySpan<byte> bytes, int start, int end)
    {
        bool leftIsWord = start > 0 && IsAsciiWordByte(bytes[start - 1]);
        bool rightIsWord = end < bytes.Length && IsAsciiWordByte(bytes[end]);
        return !leftIsWord && !rightIsWord;
    }

    private static bool IsAsciiWordByte(byte value)
    {
        return value == (byte)'_'
            || (value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z');
    }

    internal static int GetInclusiveMatchEnd(RegexMatch match)
    {
        return match.Length == 0 ? match.Start : match.Start + match.Length - 1;
    }

    internal static int GetLineStart(ReadOnlySpan<byte> bytes, int offset)
    {
        int boundedOffset = Math.Clamp(offset, 0, bytes.Length);
        for (int index = boundedOffset - 1; index >= 0; index--)
        {
            if (bytes[index] == (byte)'\n')
            {
                return index + 1;
            }
        }

        return 0;
    }

    internal static int GetLineEnd(ReadOnlySpan<byte> bytes, int lineStart)
    {
        int boundedStart = Math.Clamp(lineStart, 0, bytes.Length);
        int relativeEnd = bytes[boundedStart..].IndexOf((byte)'\n');
        return relativeEnd < 0 ? bytes.Length : boundedStart + relativeEnd + 1;
    }

    private static int GetLineContentEnd(ReadOnlySpan<byte> bytes, int lineStart)
    {
        int lineEnd = GetLineEnd(bytes, lineStart);
        return lineEnd > lineStart && bytes[lineEnd - 1] == (byte)'\n'
            ? lineEnd - 1
            : lineEnd;
    }

    internal static int GetNextLineStart(int lineEnd, int length)
    {
        return lineEnd < length ? lineEnd : length + 1;
    }

    internal static long GetLineNumber(ReadOnlySpan<byte> bytes, int lineStart)
    {
        return 1 + CountLineFeeds(bytes[..Math.Clamp(lineStart, 0, bytes.Length)]);
    }

    internal static long CountLineFeeds(ReadOnlySpan<byte> bytes)
    {
        return ByteCounter.Count(bytes, (byte)'\n');
    }

}
