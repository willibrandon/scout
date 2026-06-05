using System;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Scout;

/// <summary>
/// Generates compressed artifact payload classes from explicitly marked additional files.
/// </summary>
[Generator]
public sealed class GeneratedArtifactSourceGenerator : IIncrementalGenerator
{
    private const string ArtifactClassMetadataName = "build_metadata.AdditionalFiles.ScoutGeneratedArtifactClass";
    private const int StringChunkLength = 120;

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GeneratedArtifactEntry> artifacts = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => TryCreateEntry(pair.Left, pair.Right, cancellationToken))
            .Where(static entry => !string.IsNullOrEmpty(entry.ClassName));

        context.RegisterSourceOutput(artifacts, static (context, entry) =>
            context.AddSource(entry.HintName, SourceText.From(GenerateSource(entry), Encoding.UTF8)));
    }

    private static GeneratedArtifactEntry TryCreateEntry(
        AdditionalText additionalText,
        AnalyzerConfigOptionsProvider optionsProvider,
        CancellationToken cancellationToken)
    {
        AnalyzerConfigOptions options = optionsProvider.GetOptions(additionalText);
        if (!options.TryGetValue(ArtifactClassMetadataName, out string? className) ||
            className is null ||
            !IsIdentifier(className))
        {
            return default;
        }

        SourceText? text = additionalText.GetText(cancellationToken);
        if (text is null)
        {
            return default;
        }

        string base64 = StripWhitespace(text.ToString());
        if (base64.Length == 0)
        {
            return default;
        }

        return new GeneratedArtifactEntry(className, base64);
    }

    private static string StripWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            if (!IsIdentifierPart(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char character)
    {
        return character == '_' || char.IsLetter(character);
    }

    private static bool IsIdentifierPart(char character)
    {
        return character == '_' || char.IsLetterOrDigit(character);
    }

    private static string GenerateSource(GeneratedArtifactEntry entry)
    {
        var builder = new StringBuilder(entry.Base64.Length + 512);
        builder.Append("namespace Scout;\n");
        builder.Append('\n');
        builder.Append("/// <summary>\n");
        builder.Append("/// Provides a source-generated compressed Scout artifact payload.\n");
        builder.Append("/// </summary>\n");
        builder.Append("internal static class ");
        builder.Append(entry.ClassName);
        builder.Append('\n');
        builder.Append("{\n");
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Gets the artifact payload as gzip-compressed Base64.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    internal const string CompressedBase64 =\n");
        AppendStringChunks(builder, entry.Base64);
        builder.Append("}\n");
        return builder.ToString();
    }

    private static void AppendStringChunks(StringBuilder builder, string value)
    {
        for (int offset = 0; offset < value.Length; offset += StringChunkLength)
        {
            int count = Math.Min(StringChunkLength, value.Length - offset);
            builder.Append("        \"");
            builder.Append(EscapeString(value.Substring(offset, count)));
            builder.Append(offset + count == value.Length ? "\";\n" : "\" +\n");
        }
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
