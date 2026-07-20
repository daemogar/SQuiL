using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SQuiL.VisualStudioExtension.Parsing;

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
    SuppressDebug,
    EnvironmentName,
    AsOfDate,
    Unknown,
}

public sealed class TableColumn
{
    public string Name { get; set; } = "";
    public string SqlType { get; set; } = "";
    public bool Nullable { get; set; } = false;
    /// <summary>"NULL", "NOT NULL", or null when unspecified.</summary>
    public string? NullabilityMarker { get; set; }
    /// <summary>Raw <c>DEFAULT &lt;literal&gt;</c> value (string literals keep their single quotes), or null.</summary>
    public string? DefaultValue { get; set; }
    /// <summary><c>true</c> when the column was declared <c>PRIMARY KEY</c> — its name becomes the
    /// table's relationship key for nested-object linking.</summary>
    public bool IsPrimaryKey { get; set; } = false;
    /// <summary>0-based source line of the column NAME token — multi-line-<c>table(...)</c>-precise.</summary>
    public int Line { get; set; }
    /// <summary>0-based source character of the column NAME token on its own line.</summary>
    public int Character { get; set; }
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
    public bool Nullable { get; set; } = false;
    /// <summary>"NULL", "NOT NULL", or null when unspecified.  Always null for TABLE variables (nullability is per-column).</summary>
    public string? NullabilityMarker { get; set; }
    /// <summary>0-based source character of the nullability marker keyword itself (same line as
    /// <see cref="Line"/>), when <see cref="NullabilityMarker"/> is set — lets SP0037 squiggle the
    /// exact keyword.</summary>
    public int? NullabilityMarkerCharacter { get; set; }
    /// <summary>Length of the marker keyword text as written ("NULL" or "NOT NULL"), for the squiggle range.</summary>
    public int? NullabilityMarkerLength { get; set; }
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
    /// <summary>SP-prefixed diagnostic code, e.g. "SP0017".</summary>
    public string? Code { get; set; }
    /// <summary>Line of the first (related) declaration site for two-location diagnostics.</summary>
    public int? RelatedLine { get; set; }
    public int? RelatedStartChar { get; set; }
    public int? RelatedEndChar { get; set; }
    public string? RelatedMessage { get; set; }
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

    private static readonly Regex ColumnHead = new(
        @"^(\w+)\s+(\w+(?:\([^)]*\))?)\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex NotNullModifier = new(
        @"^NOT\s+NULL\b\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NullModifier = new(
        @"^NULL\b\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PrimaryKeyModifier = new(
        @"^PRIMARY\s+KEY\b\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DefaultModifier = new(
        @"^DEFAULT\s+('[^']*'|\S+)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

                // If a TABLE type starts here but the closing ) is on a later line, collect it.
                // Tracks paren DEPTH (not "does this line contain a )") so a column whose
                // type itself carries parens (varchar(50), decimal(18,2), …) on an earlier
                // line doesn't fool the join into stopping before the table's real closing
                // paren — that silently dropped every column declared after it. Mirrors the
                // depth-tracking ScanTableColumnPositions below already uses correctly.
                if (TableTypePrefix.IsMatch(typeStr))
                {
                    int depth = ParenDepthDelta(typeStr);
                    int j = i + 1;
                    while (depth > 0 && j < lines.Length)
                    {
                        var seg = lines[j].Trim();
                        int semi = seg.IndexOf(';');
                        if (semi >= 0) seg = seg.Substring(0, semi);
                        typeStr += " " + seg;
                        depth += ParenDepthDelta(seg);
                        j++;
                    }
                }

                ParseVariable(varName, typeStr, i, rawLine, result, afterUse: useCount > 0, allLines: lines);
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
        VariableRole.Debug           => "Debug flag (bool on *Request when declared)",
        VariableRole.SuppressDebug   => "Suppress auto-debug flag (bool on *Request when declared; requires @Debug)",
        VariableRole.EnvironmentName => "Environment name (not a C# parameter)",
        VariableRole.AsOfDate        => "Point-in-time value (nullable typed property on *Request)",
        _                            => "Unknown — does not match SQuiL naming convention",
    };

    // ── Internal helpers ────────────────────────────────────────────────

    private static void ParseVariable(
        string rawName, string typeStr, int lineNum, string fullLine, SQuiLParseResult result, bool afterUse,
        string[] allLines)
    {
        int varStart = fullLine.IndexOf(rawName, StringComparison.Ordinal);
        string upper = rawName.ToUpperInvariant();
        bool isTable = TableTypePrefix.IsMatch(typeStr);

        VariableRole role;
        string name;

        if (upper == "@DEBUG")                  { role = VariableRole.Debug;           name = "Debug"; }
        else if (upper == "@SUPPRESSDEBUG")     { role = VariableRole.SuppressDebug;   name = "SuppressDebug"; }
        else if (upper == "@ENVIRONMENTNAME")   { role = VariableRole.EnvironmentName; name = "EnvironmentName"; }
        else if (upper == "@ASOFDATE")          { role = VariableRole.AsOfDate;        name = "AsOfDate"; }
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
                             + "Expected: @Param_*, @Params_*, @Return_*, @Returns_*, @Debug, @SuppressDebug, @EnvironmentName, @AsOfDate.",
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
        {
            columns = ParseTableColumns(tableMatch.Groups[1].Value);

            // Default fallback: the variable's own position (matches the old,
            // variable-precise-only behavior) — overwritten below when the
            // multi-line-aware scan finds precise per-column positions.
            int fallbackChar = Math.Max(0, varStart);
            foreach (var col in columns)
            {
                col.Line = lineNum;
                col.Character = fallbackChar;
            }

            var colPositions = ScanTableColumnPositions(allLines, lineNum, fallbackChar + rawName.Length);
            if (colPositions.Count == columns.Count)
            {
                for (int ci = 0; ci < columns.Count; ci++)
                {
                    columns[ci].Line = colPositions[ci].Line;
                    columns[ci].Character = colPositions[ci].Character;
                }
            }
        }

        // Scalar nullability — derived from the `= null` initializer, with the
        // standalone marker still read (for now) from the type-only portion.
        // Table variables have per-column nullability; guard with isTable.
        int eqIndex = typeStr.IndexOf('=');
        string typeOnly = eqIndex >= 0 ? typeStr.Substring(0, eqIndex) : typeStr;
        string initializer = eqIndex >= 0 ? typeStr.Substring(eqIndex + 1).Trim() : "";

        bool nullFromInitializer = !isTable && Regex.IsMatch(initializer, @"^null\b", RegexOptions.IgnoreCase);
        bool isNull    = !isTable
                      && Regex.IsMatch(typeOnly, @"\bnull\b",     RegexOptions.IgnoreCase)
                      && !Regex.IsMatch(typeOnly, @"\bnot\s+null\b", RegexOptions.IgnoreCase);
        bool isNotNull = !isTable
                      && Regex.IsMatch(typeOnly, @"\bnot\s+null\b", RegexOptions.IgnoreCase);
        string? scalarMarker = isTable ? null : isNull ? "NULL" : isNotNull ? "NOT NULL" : null;

        // Locate the marker keyword itself (for SP0037's squiggle range) by searching the raw
        // line starting just after the variable name — scalar DECLAREs are always single-line.
        int? nullabilityMarkerCharacter = null;
        int? nullabilityMarkerLength = null;
        if (scalarMarker is not null)
        {
            int searchFrom = varStart >= 0 ? varStart + rawName.Length : 0;
            string rest = fullLine.Substring(Math.Min(searchFrom, fullLine.Length));
            var markerPattern = scalarMarker == "NOT NULL" ? @"\bnot\s+null\b" : @"\bnull\b";
            var match = Regex.Match(rest, markerPattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                nullabilityMarkerCharacter = searchFrom + match.Index;
                nullabilityMarkerLength = match.Length;
            }
        }

        result.Variables.Add(new SQuiLVariable
        {
            Role              = role,
            RawName           = rawName,
            Name              = name,
            SqlType           = isTable ? "TABLE" : typeStr.TrimEnd(';').Trim(),
            Columns           = columns,
            Nullable          = nullFromInitializer || scalarMarker == "NULL",
            NullabilityMarker = scalarMarker,
            NullabilityMarkerCharacter = nullabilityMarkerCharacter,
            NullabilityMarkerLength    = nullabilityMarkerLength,
            Line              = lineNum,
            Character         = Math.Max(0, varStart),
        });
    }

    private static List<TableColumn> ParseTableColumns(string columnsStr)
    {
        var cols = new List<TableColumn>();
        foreach (string part in SplitTopLevelCommas(columnsStr))
        {
            string trimmed = part.Trim();
            var head = ColumnHead.Match(trimmed);
            if (!head.Success) continue;

            string? nullabilityMarker = null;
            bool isPrimaryKey = false;
            string? defaultValue = null;

            // Peel optional column modifiers in any order: null marker, Primary Key,
            // default — mirrors the generator's tokenizer-driven peeling loop.
            string tail = head.Groups[3].Value.Trim();
            while (tail.Length > 0)
            {
                var notNull = NotNullModifier.Match(tail);
                var nullOnly = notNull.Success ? Match.Empty : NullModifier.Match(tail);
                var primaryKey = (notNull.Success || nullOnly.Success) ? Match.Empty : PrimaryKeyModifier.Match(tail);
                var defaultMatch = (notNull.Success || nullOnly.Success || primaryKey.Success) ? Match.Empty : DefaultModifier.Match(tail);

                if (notNull.Success) { nullabilityMarker = "NOT NULL"; tail = tail.Substring(notNull.Length); }
                else if (nullOnly.Success) { nullabilityMarker = "NULL"; tail = tail.Substring(nullOnly.Length); }
                else if (primaryKey.Success) { isPrimaryKey = true; tail = tail.Substring(primaryKey.Length); }
                else if (defaultMatch.Success) { defaultValue = defaultMatch.Groups[1].Value; tail = tail.Substring(defaultMatch.Length); }
                else break;
            }

            cols.Add(new TableColumn
            {
                Name              = head.Groups[1].Value,
                SqlType           = head.Groups[2].Value.Trim(),
                Nullable          = nullabilityMarker == "NULL",
                NullabilityMarker = nullabilityMarker,
                DefaultValue      = defaultValue,
                IsPrimaryKey      = isPrimaryKey,
            });
        }
        return cols;
    }

    /// <summary>Net change in paren depth across a string ('(' count minus ')' count).
    /// Used to find the real end of a multi-line <c>TABLE( ... )</c> declaration without
    /// being fooled by a column type's own parens (e.g. <c>varchar(50)</c>).</summary>
    private static int ParenDepthDelta(string s)
    {
        int delta = 0;
        foreach (char ch in s)
        {
            if (ch == '(') delta++;
            else if (ch == ')') delta--;
        }
        return delta;
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

    private static readonly Regex TableOpenParen = new(
        @"\bTABLE\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Scans the ORIGINAL source lines (not the joined/trimmed <paramref name="typeStr"/>)
    /// for a <c>table( ... )</c> declaration starting at (<paramref name="startLine"/>,
    /// <paramref name="startChar"/>) and returns the source (line, character) position of
    /// each top-level column NAME token, in declaration order — correct even when the
    /// table spans multiple lines.
    ///
    /// Nested parens (e.g. <c>decimal(18,2)</c>) are tracked via paren depth so their
    /// commas are never mistaken for column separators (only depth==1 commas split columns).
    /// </summary>
    private static List<(int Line, int Character)> ScanTableColumnPositions(
        string[] lines, int startLine, int startChar)
    {
        var results = new List<(int Line, int Character)>();

        // Flatten the source from (startLine, startChar) to EOF into one string, with a
        // parallel map from flattened index -> (line, character) in the original source,
        // so multi-line declarations still yield real positions.
        var flat = new System.Text.StringBuilder();
        var map = new List<(int Line, int Character)>();
        for (int li = startLine; li < lines.Length; li++)
        {
            string content = lines[li];
            int begin = li == startLine ? Math.Min(Math.Max(startChar, 0), content.Length) : 0;
            for (int ci = begin; ci < content.Length; ci++)
            {
                flat.Append(content[ci]);
                map.Add((li, ci));
            }
            if (li < lines.Length - 1)
            {
                flat.Append('\n');
                map.Add((li, content.Length));
            }
        }

        string text = flat.ToString();
        var openMatch = TableOpenParen.Match(text);
        if (!openMatch.Success) return results;

        int idx = openMatch.Index + openMatch.Length; // just past the opening '('
        int depth = 1;
        bool atSegmentStart = true;

        while (idx < text.Length && depth > 0)
        {
            if (atSegmentStart)
            {
                while (idx < text.Length && char.IsWhiteSpace(text[idx])) idx++;
                if (idx >= text.Length) break;

                int nameStart = idx;
                while (idx < text.Length && (char.IsLetterOrDigit(text[idx]) || text[idx] == '_')) idx++;
                if (idx > nameStart) results.Add(map[nameStart]);

                atSegmentStart = false;
                continue;
            }

            char c = text[idx];
            if (c == '(') { depth++; idx++; continue; }
            if (c == ')') { depth--; idx++; if (depth == 0) break; continue; }
            if (c == ',' && depth == 1) { atSegmentStart = true; idx++; continue; }
            idx++;
        }

        return results;
    }
}
