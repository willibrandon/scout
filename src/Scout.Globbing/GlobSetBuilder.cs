using System;
using System.Collections.Generic;

namespace Scout;

/// <summary>
/// Builds ordered sets of byte-oriented glob patterns.
/// </summary>
public sealed class GlobSetBuilder
{
    private readonly List<Glob> globs = [];

    /// <summary>
    /// Gets the number of globs currently in the builder.
    /// </summary>
    public int Count => globs.Count;

    /// <summary>
    /// Adds a glob to the set being built.
    /// </summary>
    /// <param name="glob">The glob to add.</param>
    /// <returns>This builder.</returns>
    public GlobSetBuilder Add(Glob glob)
    {
        ArgumentNullException.ThrowIfNull(glob);

        globs.Add(glob);
        return this;
    }

    /// <summary>
    /// Builds a glob set from all globs added so far.
    /// </summary>
    /// <returns>A glob set containing the builder's globs in insertion order.</returns>
    public GlobSet Build()
    {
        return GlobSet.Create(globs);
    }
}
