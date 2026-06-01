using Microsoft.CodeAnalysis;

namespace Scout;

internal static class DiagnosticDescriptors
{
    internal static readonly DiagnosticDescriptor MoreThanOneType = new(
        id: "SCOUT0001",
        title: "Files must declare exactly one type",
        messageFormat: "File '{0}' declares {1} types; Scout requires one type per file, including nested types",
        category: "Scout.Structure",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TypeNameDoesNotMatchFileName = new(
        id: "SCOUT0002",
        title: "Type name must match file name",
        messageFormat: "Type '{0}' must live in a file named '{0}.cs'",
        category: "Scout.Structure",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NamespaceDoesNotMatchFolderStructure = new(
        id: "SCOUT0003",
        title: "Namespace must match folder structure",
        messageFormat: "Namespace \"{0}\" does not match folder structure, expected \"{1}\"",
        category: "Scout.Structure",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TestWaiverIsForbidden = new(
        id: "SCOUT0004",
        title: "Tests must not be skipped, ignored, explicit, or quarantined",
        messageFormat: "Test waiver '{0}' is forbidden by Scout's no-skip policy",
        category: "Scout.Structure",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor FlagOrderIsRequired = new(
        id: "SCOUT0005",
        title: "Flag definitions must declare their pinned upstream order",
        messageFormat: "Flag definition '{0}' must be annotated with [FlagOrder(<pinned upstream index>)]",
        category: "Scout.Structure",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
