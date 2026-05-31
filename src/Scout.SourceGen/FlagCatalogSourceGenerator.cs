using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Scout;

/// <summary>
/// Generates Scout's deterministic command-line flag catalog from <c>IFlag&lt;T&gt;</c> definitions.
/// </summary>
[Generator]
public sealed class FlagCatalogSourceGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<FlagCatalogEntry> flags = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { BaseList: not null },
                static (context, _) => TryCreateEntry(context))
            .Where(static entry => !string.IsNullOrEmpty(entry.FullyQualifiedName));

        context.RegisterSourceOutput(flags.Collect(), static (context, entries) => Generate(context, entries));
    }

    private static FlagCatalogEntry TryCreateEntry(GeneratorSyntaxContext context)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol symbol)
        {
            return default;
        }

        if (!ImplementsFlagInterface(symbol))
        {
            return default;
        }

        return new FlagCatalogEntry(symbol.Name, symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static bool ImplementsFlagInterface(INamedTypeSymbol symbol)
    {
        for (int index = 0; index < symbol.AllInterfaces.Length; index++)
        {
            INamedTypeSymbol interfaceSymbol = symbol.AllInterfaces[index];
            if (interfaceSymbol.Name != "IFlag" || interfaceSymbol.TypeArguments.Length != 1)
            {
                continue;
            }

            string? containingNamespace = interfaceSymbol.ContainingNamespace?.ToDisplayString();
            if (string.Equals(containingNamespace, "Scout", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<FlagCatalogEntry> entries)
    {
        if (entries.Length == 0)
        {
            return;
        }

        string source = GenerateCatalog(DeduplicateAndSort(entries));
        context.AddSource("GeneratedFlagCatalog.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static ImmutableArray<FlagCatalogEntry> DeduplicateAndSort(ImmutableArray<FlagCatalogEntry> entries)
    {
        var entriesByName = new Dictionary<string, FlagCatalogEntry>(StringComparer.Ordinal);
        for (int index = 0; index < entries.Length; index++)
        {
            entriesByName[entries[index].FullyQualifiedName] = entries[index];
        }

        var sorted = new List<FlagCatalogEntry>(entriesByName.Values);
        sorted.Sort(CompareByPinnedUpstreamOrder);

        ImmutableArray<FlagCatalogEntry>.Builder builder = ImmutableArray.CreateBuilder<FlagCatalogEntry>(sorted.Count);
        for (int index = 0; index < sorted.Count; index++)
        {
            builder.Add(sorted[index]);
        }

        return builder.ToImmutable();
    }

    private static int CompareByPinnedUpstreamOrder(FlagCatalogEntry left, FlagCatalogEntry right)
    {
        int order = GetPinnedUpstreamOrder(left.TypeName).CompareTo(GetPinnedUpstreamOrder(right.TypeName));
        return order != 0
            ? order
            : string.Compare(left.FullyQualifiedName, right.FullyQualifiedName, StringComparison.Ordinal);
    }

    private static int GetPinnedUpstreamOrder(string typeName)
    {
        return typeName switch
        {
            "RegexpFlag" => 0,
            "FileFlag" => 1,
            "AfterContextFlag" => 2,
            "BeforeContextFlag" => 3,
            "BinaryFlag" => 4,
            "BlockBufferedFlag" => 5,
            "ByteOffsetFlag" => 6,
            "CaseSensitiveFlag" => 7,
            "ColorFlag" => 8,
            "ColorsFlag" => 9,
            "ColumnFlag" => 10,
            "ContextFlag" => 11,
            "ContextSeparatorFlag" => 12,
            "CountFlag" => 13,
            "CountMatchesFlag" => 14,
            "CrlfFlag" => 15,
            "DebugFlag" => 16,
            "DfaSizeLimitFlag" => 17,
            "EncodingFlag" => 18,
            "EngineFlag" => 19,
            "FieldContextSeparatorFlag" => 20,
            "FieldMatchSeparatorFlag" => 21,
            "FilesFlag" => 22,
            "FilesWithMatchesFlag" => 23,
            "FilesWithoutMatchFlag" => 24,
            "FixedStringsFlag" => 25,
            "FollowFlag" => 26,
            "GenerateFlag" => 27,
            "GlobFlag" => 28,
            "GlobCaseInsensitiveFlag" => 29,
            "HeadingFlag" => 30,
            "HelpFlag" => 31,
            "HiddenFlag" => 32,
            "HostnameBinFlag" => 33,
            "HyperlinkFormatFlag" => 34,
            "IglobFlag" => 35,
            "IgnoreCaseFlag" => 36,
            "IgnoreFileFlag" => 37,
            "IgnoreFileCaseInsensitiveFlag" => 38,
            "IncludeZeroFlag" => 39,
            "InvertMatchFlag" => 40,
            "JsonFlag" => 41,
            "LineBufferedFlag" => 42,
            "LineNumberFlag" => 43,
            "LineNumberNoFlag" => 44,
            "LineRegexpFlag" => 45,
            "MaxColumnsFlag" => 46,
            "MaxColumnsPreviewFlag" => 47,
            "MaxCountFlag" => 48,
            "MaxDepthFlag" => 49,
            "MaxFilesizeFlag" => 50,
            "MmapFlag" => 51,
            "MultilineFlag" => 52,
            "MultilineDotallFlag" => 53,
            "NoConfigFlag" => 54,
            "IgnoreFlag" => 55,
            "IgnoreDotFlag" => 56,
            "IgnoreExcludeFlag" => 57,
            "IgnoreFilesFlag" => 58,
            "IgnoreGlobalFlag" => 59,
            "IgnoreMessagesFlag" => 60,
            "IgnoreParentFlag" => 61,
            "IgnoreVcsFlag" => 62,
            "MessagesFlag" => 63,
            "RequireGitFlag" => 64,
            "UnicodeFlag" => 65,
            "NullFlag" => 66,
            "NullDataFlag" => 67,
            "OneFileSystemFlag" => 68,
            "OnlyMatchingFlag" => 69,
            "PathSeparatorFlag" => 70,
            "PassthruFlag" => 71,
            "Pcre2Flag" => 72,
            "Pcre2VersionFlag" => 73,
            "PreFlag" => 74,
            "PreGlobFlag" => 75,
            "PrettyFlag" => 76,
            "QuietFlag" => 77,
            "RegexSizeLimitFlag" => 78,
            "ReplaceFlag" => 79,
            "SearchZipFlag" => 80,
            "SmartCaseFlag" => 81,
            "SortFlag" => 82,
            "SortrFlag" => 83,
            "StatsFlag" => 84,
            "StopOnNonmatchFlag" => 85,
            "TextFlag" => 86,
            "ThreadsFlag" => 87,
            "TraceFlag" => 88,
            "TrimFlag" => 89,
            "TypeFlag" => 90,
            "TypeNotFlag" => 91,
            "TypeAddFlag" => 92,
            "TypeClearFlag" => 93,
            "TypeListFlag" => 94,
            "UnrestrictedFlag" => 95,
            "VersionFlag" => 96,
            "VimgrepFlag" => 97,
            "WithFilenameFlag" => 98,
            "WithFilenameNoFlag" => 99,
            "WordRegexpFlag" => 100,
            "AutoHybridRegexFlag" => 101,
            "Pcre2UnicodeFlag" => 102,
            "SortFilesFlag" => 103,
            _ => int.MaxValue,
        };
    }

    private static string GenerateCatalog(ImmutableArray<FlagCatalogEntry> entries)
    {
        var builder = new StringBuilder();
        builder.Append("using System;\n");
        builder.Append('\n');
        builder.Append("namespace Scout;\n");
        builder.Append('\n');
        builder.Append("/// <summary>\n");
        builder.Append("/// Provides the deterministic generated command-line flag catalog.\n");
        builder.Append("/// </summary>\n");
        builder.Append("internal static class GeneratedFlagCatalog\n");
        builder.Append("{\n");
        builder.Append("    private static readonly global::Scout.FlagDescriptor[] descriptors =\n");
        builder.Append("    [\n");
        for (int index = 0; index < entries.Length; index++)
        {
            builder.Append("        ");
            builder.Append(entries[index].FullyQualifiedName);
            builder.Append(".Descriptor,\n");
        }

        builder.Append("    ];\n");
        builder.Append('\n');
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Gets every generated flag descriptor in deterministic order.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    internal static ReadOnlySpan<global::Scout.FlagDescriptor> Descriptors => descriptors;\n");
        builder.Append('\n');
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Finds a generated special-mode flag by long spelling.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    internal static bool TryFindLongSpecial(string name, out global::Scout.FlagDescriptor descriptor)\n");
        builder.Append("    {\n");
        builder.Append("        for (int index = 0; index < descriptors.Length; index++)\n");
        builder.Append("        {\n");
        builder.Append("            if (descriptors[index].Kind == global::Scout.FlagKind.Special && descriptors[index].MatchesLongName(name))\n");
        builder.Append("            {\n");
        builder.Append("                descriptor = descriptors[index];\n");
        builder.Append("                return true;\n");
        builder.Append("            }\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        descriptor = null!;\n");
        builder.Append("        return false;\n");
        builder.Append("    }\n");
        builder.Append('\n');
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Finds a generated special-mode flag by short spelling.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    internal static bool TryFindShortSpecial(char name, out global::Scout.FlagDescriptor descriptor)\n");
        builder.Append("    {\n");
        builder.Append("        for (int index = 0; index < descriptors.Length; index++)\n");
        builder.Append("        {\n");
        builder.Append("            if (descriptors[index].Kind == global::Scout.FlagKind.Special && descriptors[index].ShortName == name)\n");
        builder.Append("            {\n");
        builder.Append("                descriptor = descriptors[index];\n");
        builder.Append("                return true;\n");
        builder.Append("            }\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        descriptor = null!;\n");
        builder.Append("        return false;\n");
        builder.Append("    }\n");
        builder.Append('\n');
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Finds a generated no-value switch by long spelling.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    internal static bool TryFindLongSwitch(string name, out global::Scout.FlagDescriptor descriptor)\n");
        builder.Append("    {\n");
        builder.Append("        for (int index = 0; index < descriptors.Length; index++)\n");
        builder.Append("        {\n");
        builder.Append("            if (descriptors[index].Kind == global::Scout.FlagKind.Switch && descriptors[index].MatchesLongName(name))\n");
        builder.Append("            {\n");
        builder.Append("                descriptor = descriptors[index];\n");
        builder.Append("                return true;\n");
        builder.Append("            }\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        for (int index = 0; index < descriptors.Length; index++)\n");
        builder.Append("        {\n");
        builder.Append("            if (descriptors[index].CanApplySwitch && descriptors[index].MatchesNegatedName(name))\n");
        builder.Append("            {\n");
        builder.Append("                descriptor = descriptors[index];\n");
        builder.Append("                return true;\n");
        builder.Append("            }\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        descriptor = null!;\n");
        builder.Append("        return false;\n");
        builder.Append("    }\n");
        builder.Append('\n');
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Finds a generated no-value switch by short spelling.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    internal static bool TryFindShortSwitch(char name, out global::Scout.FlagDescriptor descriptor)\n");
        builder.Append("    {\n");
        builder.Append("        for (int index = 0; index < descriptors.Length; index++)\n");
        builder.Append("        {\n");
        builder.Append("            if (descriptors[index].Kind == global::Scout.FlagKind.Switch && descriptors[index].ShortName == name)\n");
        builder.Append("            {\n");
        builder.Append("                descriptor = descriptors[index];\n");
        builder.Append("                return true;\n");
        builder.Append("            }\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        for (int index = 0; index < descriptors.Length; index++)\n");
        builder.Append("        {\n");
        builder.Append("            if (descriptors[index].CanApplySwitch && descriptors[index].MatchesNegatedShortName(name))\n");
        builder.Append("            {\n");
        builder.Append("                descriptor = descriptors[index];\n");
        builder.Append("                return true;\n");
        builder.Append("            }\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        descriptor = null!;\n");
        builder.Append("        return false;\n");
        builder.Append("    }\n");
        builder.Append('\n');
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Finds a generated required-value flag by long spelling.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    internal static bool TryFindLongValue(string name, out global::Scout.FlagDescriptor descriptor)\n");
        builder.Append("    {\n");
        builder.Append("        for (int index = 0; index < descriptors.Length; index++)\n");
        builder.Append("        {\n");
        builder.Append("            if (descriptors[index].Kind == global::Scout.FlagKind.Value && descriptors[index].MatchesLongName(name))\n");
        builder.Append("            {\n");
        builder.Append("                descriptor = descriptors[index];\n");
        builder.Append("                return true;\n");
        builder.Append("            }\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        descriptor = null!;\n");
        builder.Append("        return false;\n");
        builder.Append("    }\n");
        builder.Append('\n');
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Finds a generated required-value flag by short spelling.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    internal static bool TryFindShortValue(char name, out global::Scout.FlagDescriptor descriptor)\n");
        builder.Append("    {\n");
        builder.Append("        for (int index = 0; index < descriptors.Length; index++)\n");
        builder.Append("        {\n");
        builder.Append("            if (descriptors[index].Kind == global::Scout.FlagKind.Value && descriptors[index].ShortName == name)\n");
        builder.Append("            {\n");
        builder.Append("                descriptor = descriptors[index];\n");
        builder.Append("                return true;\n");
        builder.Append("            }\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        descriptor = null!;\n");
        builder.Append("        return false;\n");
        builder.Append("    }\n");
        builder.Append("}\n");
        return builder.ToString();
    }
}
