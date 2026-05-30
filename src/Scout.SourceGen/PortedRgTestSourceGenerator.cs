using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Scout;

/// <summary>
/// Generates one xUnit test wrapper per ported upstream ripgrep <c>rgtest!</c> case.
/// </summary>
[Generator]
public sealed class PortedRgTestSourceGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<PortedRgTestEntry>> entries = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith("PortedRgTests.catalog", StringComparison.Ordinal))
            .Select(static (file, cancellationToken) => ReadEntries(file, cancellationToken))
            .Collect()
            .Select(static (entryGroups, _) => FlattenEntries(entryGroups));

        context.RegisterSourceOutput(entries, static (context, entries) => Generate(context, entries));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<PortedRgTestEntry> entries)
    {
        if (entries.Length == 0)
        {
            return;
        }

        var usedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < entries.Length; index++)
        {
            PortedRgTestEntry entry = entries[index];
            string typeName = CreateUniqueTypeName(entry, usedTypeNames);
            string source = GenerateTestType(typeName, entry);
            context.AddSource(typeName + ".g.cs", SourceText.From(source, Encoding.UTF8));
        }

        context.AddSource("PortedRgTestCatalogCompleteness.g.cs", SourceText.From(GenerateCatalogCompleteness(entries), Encoding.UTF8));
    }

    private static ImmutableArray<PortedRgTestEntry> FlattenEntries(ImmutableArray<ImmutableArray<PortedRgTestEntry>> entryGroups)
    {
        ImmutableArray<PortedRgTestEntry>.Builder builder = ImmutableArray.CreateBuilder<PortedRgTestEntry>();
        for (int groupIndex = 0; groupIndex < entryGroups.Length; groupIndex++)
        {
            builder.AddRange(entryGroups[groupIndex]);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<PortedRgTestEntry> ReadEntries(AdditionalText file, CancellationToken cancellationToken)
    {
        SourceText? text = file.GetText(cancellationToken);
        if (text is null)
        {
            return ImmutableArray<PortedRgTestEntry>.Empty;
        }

        ImmutableArray<PortedRgTestEntry>.Builder entries = ImmutableArray.CreateBuilder<PortedRgTestEntry>();
        for (int lineIndex = 0; lineIndex < text.Lines.Count; lineIndex++)
        {
            string line = text.Lines[lineIndex].ToString().Trim();
            AddEntry(line, entries);
        }

        return entries.ToImmutable();
    }

    private static void AddEntry(string line, ImmutableArray<PortedRgTestEntry>.Builder entries)
    {
        if (line.Length == 0 || line[0] == '#')
        {
            return;
        }

        int separator = line.IndexOf('|');
        if (separator <= 0 || separator == line.Length - 1)
        {
            return;
        }

        string sourceFile = line.Substring(0, separator).Trim();
        string name = line.Substring(separator + 1).Trim();
        if (sourceFile.Length > 0 && name.Length > 0)
        {
            entries.Add(new PortedRgTestEntry(sourceFile, name));
        }
    }

    private static string CreateUniqueTypeName(PortedRgTestEntry entry, HashSet<string> usedTypeNames)
    {
        string typeName = "PortedRgTest" + ToIdentifierStem(entry.SourceFile) + ToIdentifierStem(entry.Name);
        if (usedTypeNames.Add(typeName))
        {
            return typeName;
        }

        int suffix = 2;
        while (!usedTypeNames.Add(typeName + suffix.ToString()))
        {
            suffix++;
        }

        return typeName + suffix.ToString();
    }

    private static string ToIdentifierStem(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool nextUpper = true;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9'))
            {
                if (builder.Length == 0 && character >= '0' && character <= '9')
                {
                    builder.Append('_');
                }

                builder.Append(nextUpper ? ToUpperInvariantAscii(character) : character);
                nextUpper = false;
            }
            else
            {
                nextUpper = true;
            }
        }

        return builder.Length == 0 ? "Case" : builder.ToString();
    }

    private static char ToUpperInvariantAscii(char character)
    {
        return character >= 'a' && character <= 'z' ? (char)(character - 32) : character;
    }

    private static string GenerateTestType(string typeName, PortedRgTestEntry entry)
    {
        string sourceFile = EscapeString(entry.SourceFile);
        string name = EscapeString(entry.Name);
        return
            "namespace Scout;\n" +
            "\n" +
            "/// <summary>\n" +
            "/// Runs the ported upstream ripgrep <c>" + sourceFile + "::" + name + "</c> case.\n" +
            "/// </summary>\n" +
            "public sealed class " + typeName + "\n" +
            "{\n" +
            "    /// <summary>\n" +
            "    /// Verifies this upstream case against pinned ripgrep.\n" +
            "    /// </summary>\n" +
            "    [global::Xunit.FactAttribute]\n" +
            "    public void MatchesPinnedRipgrep()\n" +
            "    {\n" +
            "        PortedRgTests.Run(\"" + sourceFile + "\", \"" + name + "\");\n" +
            "    }\n" +
            "}\n";
    }

    private static string GenerateCatalogCompleteness(ImmutableArray<PortedRgTestEntry> entries)
    {
        var builder = new StringBuilder();
        builder.Append("namespace Scout;\n");
        builder.Append('\n');
        builder.Append("/// <summary>\n");
        builder.Append("/// Verifies the generated ported ripgrep test catalog matches the runtime catalog.\n");
        builder.Append("/// </summary>\n");
        builder.Append("public sealed class PortedRgTestCatalogCompleteness\n");
        builder.Append("{\n");
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Verifies all cataloged upstream cases are generated as tests.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    [global::Xunit.FactAttribute]\n");
        builder.Append("    public void CatalogMatchesGeneratedTests()\n");
        builder.Append("    {\n");
        builder.Append("        PortedRgTests.AssertCatalog(new (string SourceFile, string Name)[]\n");
        builder.Append("        {\n");
        for (int index = 0; index < entries.Length; index++)
        {
            builder.Append("            (\"");
            builder.Append(EscapeString(entries[index].SourceFile));
            builder.Append("\", \"");
            builder.Append(EscapeString(entries[index].Name));
            builder.Append("\"),\n");
        }

        builder.Append("        });\n");
        builder.Append("    }\n");
        builder.Append("}\n");
        return builder.ToString();
    }

    private static string EscapeString(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character == '\\' || character == '"')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
