using System.Text;

namespace Scout;

internal static class CliHyperlinkFormatParser
{
    private const string AliasNames = "default, none, cursor, file, grep+, kitty, macvim, textmate, vscode, vscode-insiders, vscodium";

    public static bool TryParse(string value, out string normalized, out string? error)
    {
        ArgumentNullException.ThrowIfNull(value);
        string input = ResolveAlias(value);
        var text = new StringBuilder();
        var firstText = new StringBuilder();
        var variable = new StringBuilder();
        bool hasVariable = false;
        bool hasPath = false;
        bool hasLine = false;
        bool hasColumn = false;
        bool beforeFirstVariable = true;
        int state = 0;

        for (int index = 0; index < input.Length; index++)
        {
            char c = input[index];
            switch (state)
            {
                case 0:
                    if (c == '{')
                    {
                        state = 2;
                    }
                    else if (c == '}')
                    {
                        state = 1;
                    }
                    else
                    {
                        AppendText(text, firstText, c, beforeFirstVariable);
                    }

                    break;

                case 1:
                    if (c != '}')
                    {
                        normalized = string.Empty;
                        error = "unopened variable: found '}' without a corresponding '{' preceding it";
                        return false;
                    }

                    AppendText(text, firstText, '}', beforeFirstVariable);
                    state = 0;
                    break;

                case 2:
                    if (c == '{')
                    {
                        AppendText(text, firstText, '{', beforeFirstVariable);
                        state = 0;
                    }
                    else
                    {
                        variable.Clear();
                        if (c == '}')
                        {
                            if (!AppendVariable(text, variable.ToString(), ref hasVariable, ref hasPath, ref hasLine, ref hasColumn, out error))
                            {
                                normalized = string.Empty;
                                return false;
                            }

                            beforeFirstVariable = false;
                            state = 0;
                        }
                        else
                        {
                            variable.Append(c);
                            state = 3;
                        }
                    }

                    break;

                case 3:
                    if (c == '}')
                    {
                        if (!AppendVariable(text, variable.ToString(), ref hasVariable, ref hasPath, ref hasLine, ref hasColumn, out error))
                        {
                            normalized = string.Empty;
                            return false;
                        }

                        beforeFirstVariable = false;
                        state = 0;
                    }
                    else
                    {
                        variable.Append(c);
                    }

                    break;
            }
        }

        if (state == 1)
        {
            normalized = string.Empty;
            error = "unopened variable: found '}' without a corresponding '{' preceding it";
            return false;
        }

        if (state is 2 or 3)
        {
            normalized = string.Empty;
            error = "unclosed variable: found '{' without a corresponding '}' following it";
            return false;
        }

        if (text.Length == 0)
        {
            normalized = string.Empty;
            error = null;
            return true;
        }

        if (!hasVariable)
        {
            normalized = string.Empty;
            error = $"at least a {{path}} variable is required in a hyperlink format, or otherwise use a valid alias: {AliasNames}";
            return false;
        }

        if (!hasPath)
        {
            normalized = string.Empty;
            error = "the {path} variable is required in a hyperlink format";
            return false;
        }

        if (hasColumn && !hasLine)
        {
            normalized = string.Empty;
            error = "the hyperlink format contains a {column} variable, but no {line} variable is present";
            return false;
        }

        if (!HasValidScheme(firstText.ToString()))
        {
            normalized = string.Empty;
            error = "the hyperlink format must start with a valid URL scheme, i.e., [0-9A-Za-z+-.]+:";
            return false;
        }

        normalized = text.ToString();
        error = null;
        return true;
    }

    private static void AppendText(StringBuilder text, StringBuilder firstText, char c, bool beforeFirstVariable)
    {
        text.Append(c);
        if (beforeFirstVariable)
        {
            firstText.Append(c);
        }
    }

    private static bool AppendVariable(
        StringBuilder text,
        string name,
        ref bool hasVariable,
        ref bool hasPath,
        ref bool hasLine,
        ref bool hasColumn,
        out string? error)
    {
        switch (name)
        {
            case "host":
                break;

            case "wslprefix":
                break;

            case "path":
                hasPath = true;
                break;

            case "line":
                hasLine = true;
                break;

            case "column":
                hasColumn = true;
                break;

            default:
                error = $"invalid hyperlink format variable: '{name}', choose from: path, line, column, host, wslprefix";
                return false;
        }

        hasVariable = true;
        text.Append('{');
        text.Append(name);
        text.Append('}');
        error = null;
        return true;
    }

    private static bool HasValidScheme(string text)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return false;
        }

        for (int index = 0; index < colon; index++)
        {
            char c = text[index];
            if (!((c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                c is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveAlias(string value)
    {
        return value switch
        {
            "cursor" => "cursor://file{path}:{line}:{column}",
            "default" => OperatingSystem.IsWindows() ? "file://{path}" : "file://{host}{path}",
            "file" => "file://{host}{path}",
            "grep+" => "grep+://{path}:{line}",
            "kitty" => "file://{host}{path}#{line}",
            "macvim" => "mvim://open?url=file://{path}&line={line}&column={column}",
            "none" => string.Empty,
            "textmate" => "txmt://open?url=file://{path}&line={line}&column={column}",
            "vscode" => "vscode://file{path}:{line}:{column}",
            "vscode-insiders" => "vscode-insiders://file{path}:{line}:{column}",
            "vscodium" => "vscodium://file{path}:{line}:{column}",
            _ => value,
        };
    }
}
