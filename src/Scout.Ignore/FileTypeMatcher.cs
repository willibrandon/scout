using System;
using System.Collections.Generic;
using System.Text;

namespace Scout;

/// <summary>
/// Matches directory entries against selected and negated file types.
/// </summary>
public sealed class FileTypeMatcher
{
    private readonly FileTypeDefinition[] definitions;
    private readonly FileTypePattern[] patterns;
    private readonly bool hasSelected;

    internal FileTypeMatcher(FileTypeDefinition[] definitions, FileTypePattern[] patterns, bool hasSelected)
    {
        this.definitions = definitions;
        this.patterns = patterns;
        this.hasSelected = hasSelected;
    }

    /// <summary>
    /// Gets an empty file type matcher.
    /// </summary>
    public static FileTypeMatcher Empty { get; } = new([], [], hasSelected: false);

    /// <summary>
    /// Gets a value indicating whether this matcher has no selected or negated file types.
    /// </summary>
    public bool IsEmpty => patterns.Length == 0;

    /// <summary>
    /// Gets the sorted file type definitions known to this matcher.
    /// </summary>
    public IReadOnlyList<FileTypeDefinition> Definitions => definitions;

    internal IgnoreDecision Match(DirEntry entry)
    {
        if (entry.IsDirectory || patterns.Length == 0)
        {
            return IgnoreDecision.None;
        }

        byte[] fileName = entry.IsRawUnixPath
            ? entry.UnixFileNameBytes.ToArray()
            : Encoding.UTF8.GetBytes(entry.FileName);
        FileTypePattern? matched = null;
        for (int index = 0; index < patterns.Length; index++)
        {
            if (patterns[index].IsMatch(fileName))
            {
                matched = patterns[index];
            }
        }

        if (matched is not null)
        {
            return matched.IsNegated ? IgnoreDecision.Ignore : IgnoreDecision.Whitelist;
        }

        return hasSelected ? IgnoreDecision.Ignore : IgnoreDecision.None;
    }
}
