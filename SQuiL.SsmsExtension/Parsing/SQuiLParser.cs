using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SQuiL.SsmsExtension.Parsing;

/// <summary>
/// C# port of <c>SQuiL.VSCodeExtension/src/squil/parser.ts</c>.
///
/// Extracts the minimum information the editor providers need:
///   • Query name (from a leading <c>--Name: …</c> comment)
///   • Database (from a USE statement)
///   • Variables (from DECLARE statements, classified by role)
///   • Diagnostics (missing/duplicate USE, unknown @-prefix, etc.)
///
/// The classification mirrors the TS implementation byte-for-byte so the
/// SSMS authoring experience matches VS Code's.  Any logic change here MUST
/// be matched in parser.ts (or vice versa).
/// </summary>
public enum VariableRole
{
    Param,
    Params,
    ParamTable,
    Return,
    Returns,
    ReturnTable,
    Debug,
    EnvironmentName,
    Error,
    Errors,
    Unknown,
}

public sealed class TableColumn
{
    public string Name { get; set; } = "";
    public string SqlType { get; set; } = "";
    public bool Nullable { get; set; } = true;
}

public sealed class SQuiLVariable
{
    public VariableRole Role { get; set; }
    /// <summary>Raw token as it appears in SQL, e.g. <c>@Param_Name</c>.</summary>
    public string RawName { get; set; } = "";
    /// <summary>C#-style name, e.g. <c>Name</c>.</summary>
    public string Name { get; set; } = "";
    /// <summary>SQL type string, e.g. <c>VARCHAR(100)</c> or <c>TABLE</c>.</summary>
    public string SqlType { get; set; } = "";
    /// <summary>Column definitions when this variable is a TABLE type.</summary>
    public List<TableColumn>? Columns { get; set; }
    public int Line { get; set; }
    public int Character { get; set; }
}

public enum DiagnosticSeverity { Error, Warning, Info }

public sealed class SQuiLDiagnostic
{
    public string Message { get; set; } = "";
    public int Line { get; set; }
    public int StartChar { get; set; }
    public int EndChar { get; set; }
    public DiagnosticSeverity Severity { get; set; }
}

public sealed class SQuiLParseResult
{
    public string? QueryName { get; set; }
    public string? Database { get; set; }
    public int? DatabaseLine { get; set; }
    public List<SQuiLVariable> Variables { get; } = new();
    public List<SQuiLDiagnostic> Diagnostics { get; } = new();
}

public static class SQuiLParser
{
    private static readonly Regex NameAnnotation = new(
        @"^--\s*Name:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UseStatement = new(
        @"^USE\s+\[?(\w+)\]?\s*;?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DeclareStatement = new(
        @"^DECLARE\s+(@\w+)\s+([\s\S]*?)(?:;|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TableTypePrefix = new(
        @"^TABLE\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TableTypeFull = new(
        @"TABLE\s*\((.+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex ColumnSpec = new(
        @"^(\w+)\s+(\w+(?:\([^)]*\))?)\s*(NULL|NOT\s+NULL)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SQuiLParseResult Parse(string text)
    {
        var result = new SQuiLParseResult();
        var lines = text.Split('\n');
        int useCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string rawLine = lines[i];
            string trimmed = rawLine.Trim();

            // --Name: annotation (only meaningful at the top, but we check anywhere)
            if (result.QueryName == null)
            {
                var nameMatch = NameAnnotation.Match(trimmed);
                if (nameMatch.Success)
                {
                    result.QueryName = nameMatch.Groups[1].Value.Trim();
                    continue;
                }
            }

            // Skip blank lines and pure comments
            if (string.IsNullOrEmpty(trimmed)
                || trimmed.StartsWith("--", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
                continue;

            // USE statement
            var useMatch = UseStatement.Match(trimmed);
            if (useMatch.Success)
            {
                useCount++;
                int usePos = IndexOfIgnoreCase(rawLine, "USE");
                if (useCount > 1)
                {
                    result.Diagnostics.Add(new SQuiLDiagnostic
                    {
                        Message  = "Multiple USE statements found. Only one is allowed per SQuiL file.",
                        Line     = i,
                        StartChar= Math.Max(0, usePos),
                        EndChar  = rawLine.TrimEnd().Length,
                        Severity = DiagnosticSeverity.Error,
                    });
                }
                else
                {
                    result.Database     = useMatch.Groups[1].Value;
                    result.DatabaseLine = i;
                }
                continue;
            }

            // DECLARE statement — capture the variable name and everything after it.
            // Handles multiline TABLE declarations by joining continuation if needed.
            var declareMatch = DeclareStatement.Match(trimmed);
            if (declareMatch.Success)
            {
                string varName = declareMatch.Groups[1].Value;
                string typeStr = declareMatch.Groups[2].Value.Trim();

                // If a TABLE type starts here but the closing ) is on a later line, collect it
                if (TableTypePrefix.IsMatch(typeStr) && !typeStr.Contains(")"))
                {
                    int j = i + 1;
                    while (j < lines.Length && !lines[j].Contains(")"))
                    {
                        typeStr += " " + lines[j].Trim();
                        j++;
                    }
                    if (j < lines.Length)
                    {
                        var seg = lines[j].Trim();
                        int semi = seg.IndexOf(';');
                        if (semi >= 0) seg = seg.Substring(0, semi);
                        typeStr += " " + seg;
                    }
                }

                ParseVariable(varName, typeStr, i, rawLine, result, afterUse: useCount > 0);
            }
        }

        if (useCount == 0)
        {
            result.Diagnostics.Add(new SQuiLDiagnostic
            {
                Message  = "No USE statement found. SQuiL requires a USE [DatabaseName]; statement.",
                Line     = 0,
                StartChar= 0,
                EndChar  = 0,
                Severity = DiagnosticSeverity.Warning,
            });
        }

        return result;
    }

    /// <summary>Human-readable description of a variable role.  Matches <c>describeRole</c> in parser.ts.</summary>
    public static string DescribeRole(VariableRole role) => role switch
    {
        VariableRole.Param           => "Input scalar parameter",
        VariableRole.Params          => "Input table-valued parameter (IEnumerable<T>)",
        VariableRole.ParamTable      => "Input object parameter (TABLE type)",
        VariableRole.Return          => "Output scalar variable",
        VariableRole.Returns         => "Output table (IEnumerable<T>)",
        VariableRole.ReturnTable     => "Output object (TABLE type)",
        VariableRole.Debug           => "Debug flag (not a C# parameter)",
        VariableRole.EnvironmentName => "Environment name (not a C# parameter)",
        VariableRole.Error           => "Error variable (not a C# parameter)",
        VariableRole.Errors          => "Errors collection (not a C# parameter)",
        _                            => "Unknown — does not match SQuiL naming convention",
    };

    // ── Internal helpers ────────────────────────────────────────────────

    private static void ParseVariable(
        string rawName, string typeStr, int lineNum, string fullLine, SQuiLParseResult result, bool afterUse)
    {
        int varStart = fullLine.IndexOf(rawName, StringComparison.Ordinal);
        string upper = rawName.ToUpperInvariant();
        bool isTable = TableTypePrefix.IsMatch(typeStr);

        VariableRole role;
        string name;

        if (upper == "@DEBUG")                  { role = VariableRole.Debug;           name = "Debug"; }
        else if (upper == "@ENVIRONMENTNAME")   { role = VariableRole.EnvironmentName; name = "EnvironmentName"; }
        else if (upper == "@ERROR")             { role = VariableRole.Error;           name = "Error"; }
        else if (upper == "@ERRORS")            { role = VariableRole.Errors;          name = "Errors"; }
        else if (upper.StartsWith("@PARAMS_", StringComparison.Ordinal))
        { role = VariableRole.Params; name = rawName.Substring("@Params_".Length); }
        else if (upper.StartsWith("@PARAM_", StringComparison.Ordinal))
        { role = isTable ? VariableRole.ParamTable : VariableRole.Param; name = rawName.Substring("@Param_".Length); }
        else if (upper.StartsWith("@RETURNS_", StringComparison.Ordinal))
        { role = VariableRole.Returns; name = rawName.Substring("@Returns_".Length); }
        else if (upper.StartsWith("@RETURN_", StringComparison.Ordinal))
        { role = isTable ? VariableRole.ReturnTable : VariableRole.Return; name = rawName.Substring("@Return_".Length); }
        else
        {
            role = VariableRole.Unknown;
            name = rawName.Substring(1);
            // Only I/O declarations (before the USE) must follow SQuiL naming.
            // After the USE, @-variables are ordinary T-SQL locals in the query
            // body — don't require the @Param_/@Return_ convention for them.
            if (!afterUse)
            {
                result.Diagnostics.Add(new SQuiLDiagnostic
                {
                    Message  = $"Variable '{rawName}' doesn't follow SQuiL naming conventions. "
                             + "Expected: @Param_*, @Params_*, @Return_*, @Returns_*, @Debug, @EnvironmentName, @Error, or @Errors.",
                    Line     = lineNum,
                    StartChar= Math.Max(0, varStart),
                    EndChar  = (varStart >= 0 ? varStart : 0) + rawName.Length,
                    Severity = DiagnosticSeverity.Warning,
                });
            }
        }

        List<TableColumn>? columns = null;
        var tableMatch = TableTypeFull.Match(typeStr);
        if (tableMatch.Success)
            columns = ParseTableColumns(tableMatch.Groups[1].Value);

        result.Variables.Add(new SQuiLVariable
        {
            Role      = role,
            RawName   = rawName,
            Name      = name,
            SqlType   = isTable ? "TABLE" : typeStr.TrimEnd(';').Trim(),
            Columns   = columns,
            Line      = lineNum,
            Character = Math.Max(0, varStart),
        });
    }

    private static List<TableColumn> ParseTableColumns(string columnsStr)
    {
        var cols = new List<TableColumn>();
        foreach (string part in SplitTopLevelCommas(columnsStr))
        {
            string trimmed = part.Trim();
            var match = ColumnSpec.Match(trimmed);
            if (!match.Success) continue;

            string nullability = (match.Groups[3].Value ?? "").ToUpperInvariant().Trim();
            cols.Add(new TableColumn
            {
                Name     = match.Groups[1].Value,
                SqlType  = match.Groups[2].Value.Trim(),
                Nullable = nullability != "NOT NULL",
            });
        }
        return cols;
    }

    private static IEnumerable<string> SplitTopLevelCommas(string str)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                yield return str.Substring(start, i - start);
                start = i + 1;
            }
        }
        yield return str.Substring(start);
    }

    private static int IndexOfIgnoreCase(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
}
