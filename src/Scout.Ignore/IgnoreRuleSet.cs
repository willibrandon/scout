using System.Collections.Generic;
using System.IO;

namespace Scout;

internal sealed class IgnoreRuleSet
{
    private readonly List<IgnoreRule> rules = [];

    public bool IsEmpty => rules.Count == 0;

    public void Add(IgnoreRule rule)
    {
        rules.Add(rule);
    }

    public void AddFile(string baseDirectory, string path, bool asciiCaseInsensitive)
    {
        bool firstLine = true;
        foreach (string line in File.ReadLines(path))
        {
            string currentLine = firstLine ? line.TrimStart('\uFEFF') : line;
            firstLine = false;

            if (IgnoreRule.TryParse(baseDirectory, currentLine, asciiCaseInsensitive, out IgnoreRule? rule) && rule is not null)
            {
                Add(rule);
            }
        }
    }

    public IgnoreDecision Match(DirEntry entry)
    {
        IgnoreDecision decision = IgnoreDecision.None;
        for (int index = 0; index < rules.Count; index++)
        {
            IgnoreDecision current = rules[index].Match(entry);
            if (current != IgnoreDecision.None)
            {
                decision = current;
            }
        }

        return decision;
    }

    public IgnoreDecision MatchPathOrAnyParents(DirEntry entry)
    {
        IgnoreDecision decision = Match(entry);
        if (decision != IgnoreDecision.None)
        {
            return decision;
        }

        string? parent = Path.GetDirectoryName(entry.FullPath);
        while (!string.IsNullOrEmpty(parent))
        {
            var parentEntry = new DirEntry(
                parent,
                entry.Depth,
                FileAttributes.Directory,
                isDirectory: true,
                isSymbolicLink: false,
                isStdin: false,
                length: null,
                identity: default);

            decision = Match(parentEntry);
            if (decision != IgnoreDecision.None)
            {
                return decision;
            }

            parent = Path.GetDirectoryName(parent);
        }

        return IgnoreDecision.None;
    }
}
