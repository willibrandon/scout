using System;
using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Builds file type matchers from definitions and selections.
/// </summary>
public sealed class FileTypeMatcherBuilder
{
    private readonly Dictionary<string, List<string>> definitions = new(StringComparer.Ordinal);
    private readonly List<(string Name, bool IsNegated)> selections = [];

    /// <summary>
    /// Adds default file type definitions.
    /// </summary>
    /// <returns>This builder.</returns>
    public FileTypeMatcherBuilder AddDefaults()
    {
        DefaultFileTypes.AddTo(this);
        return this;
    }

    /// <summary>
    /// Adds a glob to a file type definition.
    /// </summary>
    /// <param name="name">The file type name.</param>
    /// <param name="glob">The glob that recognizes the file type.</param>
    /// <returns>This builder.</returns>
    public FileTypeMatcherBuilder Add(string name, string glob)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrEmpty(glob);

        if (!definitions.TryGetValue(name, out List<string>? globs))
        {
            globs = [];
            definitions.Add(name, globs);
        }

        globs.Add(glob);
        return this;
    }

    /// <summary>
    /// Adds a definition in <c>name:glob</c> or <c>name:include:a,b</c> form.
    /// </summary>
    /// <param name="definition">The definition text.</param>
    /// <returns>This builder.</returns>
    public FileTypeMatcherBuilder AddDefinition(string definition)
    {
        ArgumentException.ThrowIfNullOrEmpty(definition);

        string[] parts = definition.Split(':');
        if (parts.Length == 2)
        {
            return Add(parts[0], parts[1]);
        }

        if (parts.Length == 3 && parts[1] == "include")
        {
            AddIncludeDefinition(parts[0], parts[2]);
            return this;
        }

        throw new ArgumentException("File type definitions must be in 'name:glob' or 'name:include:a,b' form.", nameof(definition));
    }

    /// <summary>
    /// Selects a file type.
    /// </summary>
    /// <param name="name">The file type name, or <c>all</c>.</param>
    /// <returns>This builder.</returns>
    public FileTypeMatcherBuilder Select(string name)
    {
        AddSelection(name, isNegated: false);
        return this;
    }

    /// <summary>
    /// Negates a file type.
    /// </summary>
    /// <param name="name">The file type name, or <c>all</c>.</param>
    /// <returns>This builder.</returns>
    public FileTypeMatcherBuilder Negate(string name)
    {
        AddSelection(name, isNegated: true);
        return this;
    }

    /// <summary>
    /// Clears the definition for a file type.
    /// </summary>
    /// <param name="name">The file type name.</param>
    /// <returns>This builder.</returns>
    public FileTypeMatcherBuilder Clear(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        definitions.Remove(name);
        return this;
    }

    /// <summary>
    /// Builds an immutable file type matcher.
    /// </summary>
    /// <returns>A file type matcher.</returns>
    public FileTypeMatcher Build()
    {
        FileTypeDefinition[] sortedDefinitions = BuildDefinitions();
        var patterns = new List<FileTypePattern>();
        bool hasSelected = false;

        for (int index = 0; index < selections.Count; index++)
        {
            (string name, bool isNegated) = selections[index];
            if (!definitions.TryGetValue(name, out List<string>? globs))
            {
                throw new InvalidOperationException($"unrecognized file type: {name}");
            }

            hasSelected |= !isNegated;
            var definition = new FileTypeDefinition(name, globs.ToArray());
            for (int globIndex = 0; globIndex < globs.Count; globIndex++)
            {
                patterns.Add(new FileTypePattern(definition, globs[globIndex], isNegated));
            }
        }

        return new FileTypeMatcher(sortedDefinitions, patterns.ToArray(), hasSelected);
    }

    private void AddIncludeDefinition(string name, string includedNames)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrEmpty(includedNames);

        string[] names = includedNames.Split(',');
        for (int index = 0; index < names.Length; index++)
        {
            if (!definitions.ContainsKey(names[index]))
            {
                throw new ArgumentException($"Included file type '{names[index]}' is not defined.", nameof(includedNames));
            }
        }

        for (int index = 0; index < names.Length; index++)
        {
            List<string> globs = definitions[names[index]];
            for (int globIndex = 0; globIndex < globs.Count; globIndex++)
            {
                Add(name, globs[globIndex]);
            }
        }
    }

    private void AddSelection(string name, bool isNegated)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == "all")
        {
            foreach (string key in definitions.Keys)
            {
                selections.Add((key, isNegated));
            }

            return;
        }

        selections.Add((name, isNegated));
    }

    private FileTypeDefinition[] BuildDefinitions()
    {
        var built = new List<FileTypeDefinition>();
        foreach (KeyValuePair<string, List<string>> pair in definitions)
        {
            string[] globs = pair.Value.ToArray();
            Array.Sort(globs, StringComparer.Ordinal);
            built.Add(new FileTypeDefinition(pair.Key, globs));
        }

        built.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return built.ToArray();
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name == "all")
        {
            throw new ArgumentException("The name 'all' is reserved.", nameof(name));
        }

        for (int index = 0; index < name.Length; index++)
        {
            if (!char.IsLetterOrDigit(name[index]))
            {
                throw new ArgumentException("File type names may only contain letters and digits.", nameof(name));
            }
        }
    }
}
