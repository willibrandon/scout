using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Scout;

/// <summary>
/// Enforces Scout's no-skip policy for hand-written and generated test sources.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoSkippedTestsAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.TestWaiverIsForbidden);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeReturn, SyntaxKind.ReturnStatement);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        if (!IsTestSource(context))
        {
            return;
        }

        var attribute = (AttributeSyntax)context.Node;
        string name = GetSimpleAttributeName(attribute.Name);
        if ((string.Equals(name, "Fact", StringComparison.Ordinal) ||
            string.Equals(name, "Theory", StringComparison.Ordinal)) &&
            TryFindNamedArgument(attribute, out string argumentName))
        {
            Report(context, attribute, name + "." + argumentName);
            return;
        }

        if (string.Equals(name, "Ignore", StringComparison.Ordinal) ||
            string.Equals(name, "Explicit", StringComparison.Ordinal))
        {
            Report(context, attribute, name);
            return;
        }

        if (string.Equals(name, "Trait", StringComparison.Ordinal) &&
            ContainsForbiddenTraitValue(attribute))
        {
            Report(context, attribute, "Trait");
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (!IsTestSource(context))
        {
            return;
        }

        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            string.Equals(memberAccess.Name.Identifier.ValueText, "Skip", StringComparison.Ordinal) &&
            IsAssertExpression(memberAccess.Expression))
        {
            Report(context, invocation, "Assert.Skip");
        }
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (!IsTestSource(context))
        {
            return;
        }

        var creation = (ObjectCreationExpressionSyntax)context.Node;
        string typeName = GetSimpleTypeName(creation.Type);
        if (string.Equals(typeName, "SkipException", StringComparison.Ordinal))
        {
            Report(context, creation, "SkipException");
        }
    }

    private static void AnalyzeReturn(SyntaxNodeAnalysisContext context)
    {
        if (!IsTestSource(context))
        {
            return;
        }

        var returnStatement = (ReturnStatementSyntax)context.Node;
        if (returnStatement.Expression is not null ||
            !TryGetOwningIfStatement(returnStatement, out IfStatementSyntax ifStatement))
        {
            return;
        }

        string condition = ifStatement.Condition.ToString();
        if (condition.Contains("TryCreateDirectorySymlink", StringComparison.Ordinal) ||
            condition.Contains("TryCreateFileSymlink", StringComparison.Ordinal))
        {
            Report(context, returnStatement, "fixture capability return");
            return;
        }

        if (condition.Contains("OperatingSystem.", StringComparison.Ordinal))
        {
            Report(context, returnStatement, "platform guard return");
        }
    }

    private static bool TryFindNamedArgument(AttributeSyntax attribute, out string argumentName)
    {
        argumentName = string.Empty;
        if (attribute.ArgumentList is null)
        {
            return false;
        }

        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            string name = argument.NameEquals?.Name.Identifier.ValueText ?? string.Empty;
            if (string.Equals(name, "Skip", StringComparison.Ordinal) ||
                string.Equals(name, "Explicit", StringComparison.Ordinal))
            {
                argumentName = name;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsForbiddenTraitValue(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is null)
        {
            return false;
        }

        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            string value = argument.Expression.ToString();
            if (value.Contains("Quarantine", StringComparison.Ordinal) ||
                value.Contains("Skipped", StringComparison.Ordinal) ||
                value.Contains("Ignored", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAssertExpression(ExpressionSyntax expression)
    {
        if (expression is IdentifierNameSyntax identifier)
        {
            return string.Equals(identifier.Identifier.ValueText, "Assert", StringComparison.Ordinal);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return string.Equals(memberAccess.Name.Identifier.ValueText, "Assert", StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryGetOwningIfStatement(ReturnStatementSyntax returnStatement, out IfStatementSyntax ifStatement)
    {
        ifStatement = null!;
        if (returnStatement.Parent is IfStatementSyntax directIf)
        {
            ifStatement = directIf;
            return true;
        }

        if (returnStatement.Parent is BlockSyntax block &&
            block.Parent is IfStatementSyntax blockIf)
        {
            ifStatement = blockIf;
            return true;
        }

        return false;
    }

    private static string GetSimpleAttributeName(NameSyntax nameSyntax)
    {
        string name = nameSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => nameSyntax.ToString(),
        };

        const string suffix = "Attribute";
        if (name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return name.Substring(0, name.Length - suffix.Length);
        }

        return name;
    }

    private static string GetSimpleTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => type.ToString(),
        };
    }

    private static bool IsTestSource(SyntaxNodeAnalysisContext context)
    {
        return IsTestProject(context) || IsTestSourcePath(context.Node.SyntaxTree.FilePath);
    }

    private static bool IsTestProject(SyntaxNodeAnalysisContext context)
    {
        return context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
            "build_property.IsTestProject",
            out string? isTestProject) &&
            string.Equals(isTestProject, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestSourcePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        string normalizedPath = filePath.Replace('\\', '/');
        return normalizedPath.Contains("/tests/", StringComparison.Ordinal);
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode node, string waiver)
    {
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.TestWaiverIsForbidden,
            node.GetLocation(),
            waiver);
        context.ReportDiagnostic(diagnostic);
    }
}
