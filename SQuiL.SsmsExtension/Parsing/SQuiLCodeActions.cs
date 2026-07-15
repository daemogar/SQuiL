using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SQuiL.SsmsExtension.Parsing;

/// <summary>
/// C# port of <c>SQuiL.VSCodeExtension/src/squil/codeActions.ts</c> — the pure
/// text-edit logic behind the nested-object PK/link authoring aids (Task 16).
///
/// Two authoring aids, both pure computations over the already-parsed file
/// (no editor dependency here — see <c>SuggestedActions/</c> for the thin
/// MEF wrapper that turns these into light-bulb actions):
///   1. "Add Primary Key" — offered on a table/object variable that declares
///      no Primary Key column yet. Inserts <c> Primary Key</c> after a
///      sensible column's type (the first <c>*ID</c>-suffixed column, else
///      the first column).
///   2. "Link to `&lt;Table&gt;` via `&lt;PK&gt;`" — offered per OTHER declared
///      table in the SAME universe (OUTPUT vs INPUT — never mixed) that has a
///      Primary Key this table doesn't already carry a matching column for.
///      Selecting one inserts a new column (same name + type as the target's
///      PK) into this table's declaration, wiring the relationship-by-convention.
///
/// Both edits are computed against the RAW source lines (not the parsed
/// summaries) so the inserted text lands at an exact (line, character)
/// position — the same "flatten + scan" approach used by
/// <see cref="SQuiLParser"/>'s ScanTableColumnPositions.
///
/// Mirrors <c>codeActions.ts</c> exactly (titles, chosen-column rule, insert
/// text, position math). Ported for both the Visual Studio and SSMS
/// extensions (the two copies differ only by namespace). Change one side,
/// change all three.
/// </summary>
internal static class SQuiLCodeActions
{
    private static readonly Regex IdSuffix = new(@"ID$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Mirrors codeActions.ts's /^(\w+)\s+([\w]+(?:\([^)]*\))?)/i — the
    // `Name <Type>` token pair at the start of a column's source text.
    private static readonly Regex ColumnNameType = new(
        @"^(\w+)\s+(\w+(?:\([^)]*\))?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TableOpenParen = new(
        @"\bTABLE\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public struct SourcePosition
    {
        public int Line;
        public int Character;
        public SourcePosition(int line, int character) { Line = line; Character = character; }
    }

    public sealed class CodeActionEdit
    {
        public string Title { get; set; } = "";
        public SourcePosition Position { get; set; }
        public string InsertText { get; set; } = "";
    }

    /// <summary>A candidate parent a child could link to: another declared
    /// table/object in the SAME universe with a Primary Key this child doesn't
    /// already carry a matching column for.</summary>
    public sealed class LinkTarget
    {
        public SQuiLVariable Parent { get; set; } = null!;
        public TableColumn PkColumn { get; set; } = null!;
    }

    /// <summary>Every declared table/object variable (both universes), for
    /// cursor-hit-testing. Mirrors <c>allTableVariables</c>.</summary>
    public static List<SQuiLVariable> AllTableVariables(SQuiLParseResult parsed) =>
        SQuiLLinter.OutputTableVariables(parsed)
            .Concat(SQuiLLinter.InputTableVariables(parsed))
            .ToList();

    /// <summary>Picks the column an "Add Primary Key" action should mark: the
    /// first column whose name ends in <c>ID</c> (case-insensitive), else the
    /// first column. Mirrors <c>chooseDefaultKeyColumn</c>.</summary>
    public static TableColumn? ChooseDefaultKeyColumn(SQuiLVariable table)
    {
        var columns = table.Columns!;
        return columns.FirstOrDefault(c => IdSuffix.IsMatch(c.Name)) ?? columns.FirstOrDefault();
    }

    /// <summary>Finds the source position immediately after a column's
    /// <c>Name &lt;Type&gt;</c> token pair (e.g. right after <c>ParentID int</c>),
    /// so callers can insert a modifier like <c> Primary Key</c> there. Returns
    /// null if the line doesn't match the expected <c>name&lt;ws&gt;type</c>
    /// shape (defensive). Mirrors <c>findColumnTypeEndPosition</c>.</summary>
    public static SourcePosition? FindColumnTypeEndPosition(string[] lines, TableColumn column)
    {
        if (column.Line < 0 || column.Line >= lines.Length) return null;
        string line = lines[column.Line];
        if (column.Character < 0 || column.Character > line.Length) return null;
        string rest = line.Substring(column.Character);
        var m = ColumnNameType.Match(rest);
        if (!m.Success) return null;
        return new SourcePosition(column.Line, column.Character + m.Value.Length);
    }

    /// <summary>Finds the source position of the closing <c>)</c> of a
    /// table/object variable's <c>TABLE( ... )</c> declaration, so callers can
    /// insert a new trailing column just before it. Handles multi-line
    /// declarations and nested parens (e.g. <c>decimal(18,2)</c>) via depth
    /// tracking, mirroring ScanTableColumnPositions. Mirrors
    /// <c>findTableCloseParenPosition</c>.</summary>
    public static SourcePosition? FindTableCloseParenPosition(string[] lines, SQuiLVariable variable)
    {
        var flat = new StringBuilder();
        var map = new List<SourcePosition>();
        for (int li = variable.Line; li < lines.Length; li++)
        {
            string content = lines[li];
            int begin = li == variable.Line
                ? System.Math.Min(System.Math.Max(variable.Character, 0), content.Length)
                : 0;
            for (int ci = begin; ci < content.Length; ci++)
            {
                flat.Append(content[ci]);
                map.Add(new SourcePosition(li, ci));
            }
            if (li < lines.Length - 1)
            {
                flat.Append('\n');
                map.Add(new SourcePosition(li, content.Length));
            }
        }

        string text = flat.ToString();
        var openMatch = TableOpenParen.Match(text);
        if (!openMatch.Success) return null;

        int idx = openMatch.Index + openMatch.Length;
        int depth = 1;
        while (idx < text.Length && depth > 0)
        {
            char c = text[idx];
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return map[idx];
            }
            idx++;
        }
        return null;
    }

    /// <summary>Returns the "line span" a table/object variable's declaration
    /// occupies — used to decide whether a cursor line falls "on" this variable
    /// for code-action purposes. Falls back to a single-line span when the
    /// closing paren can't be located. Mirrors <c>tableVariableLineSpan</c>.</summary>
    public static (int StartLine, int EndLine) TableVariableLineSpan(string[] lines, SQuiLVariable variable)
    {
        var close = FindTableCloseParenPosition(lines, variable);
        return (variable.Line, close?.Line ?? variable.Line);
    }

    /// <summary>True when <paramref name="line"/> falls within
    /// <paramref name="variable"/>'s declaration span (inclusive). Mirrors
    /// <c>isCursorOnVariable</c>.</summary>
    public static bool IsCursorOnVariable(string[] lines, SQuiLVariable variable, int line)
    {
        var (startLine, endLine) = TableVariableLineSpan(lines, variable);
        return line >= startLine && line <= endLine;
    }

    /// <summary>Builds the "Add Primary Key" edit for a table with no Primary
    /// Key, or null if the insertion point can't be located (defensive).
    /// Mirrors <c>buildAddPrimaryKeyEdit</c>.</summary>
    public static CodeActionEdit? BuildAddPrimaryKeyEdit(string[] lines, SQuiLVariable table)
    {
        var column = ChooseDefaultKeyColumn(table);
        if (column is null) return null;
        var position = FindColumnTypeEndPosition(lines, column);
        if (position is null) return null;
        return new CodeActionEdit
        {
            Title = $"SQuiL: Add Primary Key on `{column.Name}`",
            Position = position.Value,
            InsertText = " Primary Key",
        };
    }

    /// <summary>The candidate parents a child could link to: another declared
    /// table/object in the SAME universe with a Primary Key this child doesn't
    /// already carry a matching column for (excludes self, excludes
    /// already-linked parents). Mirrors <c>availableLinkTargets</c>.</summary>
    public static List<LinkTarget> AvailableLinkTargets(SQuiLParseResult parsed, SQuiLVariable child)
    {
        bool isOutput = child.Role == VariableRole.Returns || child.Role == VariableRole.ReturnTable;
        var list = isOutput
            ? SQuiLLinter.OutputTableVariables(parsed)
            : SQuiLLinter.InputTableVariables(parsed);

        var childColumnNames = new HashSet<string>(
            child.Columns!.Select(c => c.Name), System.StringComparer.OrdinalIgnoreCase);

        var targets = new List<LinkTarget>();
        foreach (var candidate in list)
        {
            if (ReferenceEquals(candidate, child)) continue;
            var pk = candidate.Columns!.FirstOrDefault(c => c.IsPrimaryKey);
            if (pk is null) continue;
            if (childColumnNames.Contains(pk.Name)) continue; // already linked
            targets.Add(new LinkTarget { Parent = candidate, PkColumn = pk });
        }
        return targets;
    }

    /// <summary>Builds the "Link to `&lt;Table&gt;` via `&lt;PK&gt;`" edit: inserts a
    /// new trailing column (same name + type as the target's Primary Key) into
    /// the child's <c>TABLE(...)</c> declaration, just before the closing paren.
    /// Mirrors <c>buildInsertLinkColumnEdit</c>.</summary>
    public static CodeActionEdit? BuildInsertLinkColumnEdit(string[] lines, SQuiLVariable child, LinkTarget target)
    {
        var position = FindTableCloseParenPosition(lines, child);
        if (position is null) return null;
        return new CodeActionEdit
        {
            Title = $"SQuiL: Link to `{target.Parent.Name}` via `{target.PkColumn.Name}`",
            Position = position.Value,
            InsertText = $", {target.PkColumn.Name} {target.PkColumn.SqlType}",
        };
    }
}
