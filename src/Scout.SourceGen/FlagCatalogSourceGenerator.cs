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

        if (!TryGetFlagOrder(symbol, out int order))
        {
            order = -1;
        }

        Location? location = symbol.Locations.Length > 0 ? symbol.Locations[0] : null;
        return new FlagCatalogEntry(symbol.Name, symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), order, location);
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

    private static bool TryGetFlagOrder(INamedTypeSymbol symbol, out int order)
    {
        ImmutableArray<AttributeData> attributes = symbol.GetAttributes();
        for (int index = 0; index < attributes.Length; index++)
        {
            AttributeData attribute = attributes[index];
            INamedTypeSymbol? attributeClass = attribute.AttributeClass;
            if (attributeClass is null ||
                attributeClass.Name != "FlagOrderAttribute" ||
                attributeClass.ContainingNamespace?.ToDisplayString() != "Scout" ||
                attribute.ConstructorArguments.Length != 1)
            {
                continue;
            }

            TypedConstant argument = attribute.ConstructorArguments[0];
            if (argument.Value is int value && value >= 0)
            {
                order = value;
                return true;
            }
        }

        order = -1;
        return false;
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<FlagCatalogEntry> entries)
    {
        if (entries.Length == 0)
        {
            return;
        }

        bool hasErrors = false;
        var entriesByOrder = new Dictionary<int, FlagCatalogEntry>();
        for (int index = 0; index < entries.Length; index++)
        {
            FlagCatalogEntry entry = entries[index];
            if (entry.Order < 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.FlagOrderIsRequired,
                    entry.Location ?? Location.None,
                    entry.TypeName));
                hasErrors = true;
            }

            if (entry.Order >= 0 && entriesByOrder.TryGetValue(entry.Order, out FlagCatalogEntry existing))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateFlagOrder,
                    entry.Location ?? Location.None,
                    existing.TypeName,
                    entry.TypeName,
                    entry.Order));
                hasErrors = true;
            }
            else if (entry.Order >= 0)
            {
                entriesByOrder.Add(entry.Order, entry);
            }
        }

        if (hasErrors)
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
        sorted.Sort(CompareByFlagDefinitionOrder);

        ImmutableArray<FlagCatalogEntry>.Builder builder = ImmutableArray.CreateBuilder<FlagCatalogEntry>(sorted.Count);
        for (int index = 0; index < sorted.Count; index++)
        {
            builder.Add(sorted[index]);
        }

        return builder.ToImmutable();
    }

    private static int CompareByFlagDefinitionOrder(FlagCatalogEntry left, FlagCatalogEntry right)
    {
        int order = left.Order.CompareTo(right.Order);
        return order != 0
            ? order
            : string.Compare(left.FullyQualifiedName, right.FullyQualifiedName, StringComparison.Ordinal);
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
