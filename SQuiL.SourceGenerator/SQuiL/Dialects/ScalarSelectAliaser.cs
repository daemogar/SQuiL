namespace SQuiL.Dialects;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// The pure text rules behind the SQL Server implicit scalar-select alias.
///
/// <para>
/// A T-SQL <c>Select @Return_X;</c> returns an UNNAMED column, so the runtime shape key
/// (<c>SQuiLBaseDataContext.ShapeKey</c>, built from <c>reader.GetName(i)</c>) is <c>":int"</c> and
/// matches no generated <c>case "x:int":</c> label. Rather than force every author to write
/// <c>As X</c>, <see cref="Rewrite"/> inserts it into the emitted command text.
/// </para>
///
/// <para>
/// SQL Server only. SQLite and PostgreSQL declare scalars as single-column temp tables, so their
/// authors write <c>Select X From Return_X</c> — a real column reference that needs no alias. See
/// <c>ISqlDialect.RewriteOutputSelects</c>, which the temp-table-header dialects no-op.
/// </para>
///
/// <para>
/// Ported to the editors as <c>scalarAliasHints.ts</c> (SP0042) / <c>lintMultiScalarSelect</c>
/// (SP0041) in <c>parser.ts</c>, and <c>LintScalarAliasHint</c> / <c>LintMultiScalarSelect</c> in
/// both <c>SQuiLLinter.cs</c> copies — change one, change all four.
/// </para>
/// </summary>
public static class ScalarSelectAliaser
{
    /// <summary>A bare single-scalar select that qualifies for an implicit alias.</summary>
    public sealed class BareSelect
    {
        /// <summary>Offset of the <c>@Return_X</c> token.</summary>
        public int VariableOffset;
        /// <summary>Length of the <c>@Return_X</c> token.</summary>
        public int VariableLength;
        /// <summary>Offset at which <c>" As &lt;DeclaredName&gt;"</c> should be inserted
        /// (immediately after the variable token).</summary>
        public int InsertOffset;
        /// <summary>The declared base name, in its declared casing.</summary>
        public string DeclaredName = "";
    }

    /// <summary>A select whose top-level column list is 2+ output-scalar references (SP0041).</summary>
    public sealed class MultiSelect
    {
        /// <summary>Offset of the <c>Select</c> keyword.</summary>
        public int SelectOffset;
        /// <summary>The declared base names referenced, in source order.</summary>
        public List<string> DeclaredNames = new();
    }

    /// <summary>
    /// Tokens that may legally follow a bare scalar select's single column. Anything NOT in this
    /// set (a comma, <c>As</c>, <c>From</c>, a dot, an operator, an open paren) means the variable
    /// is part of a larger column expression, so the select does not qualify. The set is
    /// deliberately conservative — it does not enumerate every valid T-SQL statement starter
    /// (e.g. <c>grant</c>/<c>revoke</c>/<c>deny</c>/<c>open</c>/<c>fetch</c>/<c>close</c>/
    /// <c>deallocate</c>/<c>backup</c>/<c>restore</c>/<c>dbcc</c>/<c>checkpoint</c> are omitted).
    /// An unrecognized following token simply means the scanner declines to rewrite; that is the
    /// safe direction — a missed alias is a no-op, never a false rewrite.
    /// </summary>
    private static readonly HashSet<string> StatementStarters = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "select", "insert", "update", "delete", "set", "declare", "if", "while", "begin", "end",
        "exec", "execute", "return", "print", "use", "with", "merge", "truncate", "drop", "create",
        "alter", "go", "else", "commit", "rollback", "throw", "raiserror", "waitfor",
    };

    /// <summary>
    /// Returns <paramref name="text"/> with <c>" As [&lt;DeclaredName&gt;]"</c> inserted after every
    /// qualifying bare scalar select. <paramref name="scalarsByVariableName"/> maps a LOWER-CASED
    /// full variable name (e.g. <c>"@return_count"</c>) to its declared base name
    /// (e.g. <c>"Count"</c>). Text with no qualifying select is returned unchanged (reference-equal).
    /// The alias is ALWAYS bracketed — an unbracketed alias is a T-SQL syntax error when the
    /// declared name collides with a reserved keyword (e.g. <c>Order</c>, <c>Key</c>, <c>User</c>);
    /// bracketing needs no keyword list and is safe unconditionally: <c>reader.GetName</c> strips
    /// brackets, so the runtime shape key is unaffected, and the alias-parser below already accepts
    /// a bracketed alias, so this stays idempotent against an author-written bracketed alias.
    /// </summary>
    public static string Rewrite(string text, IDictionary<string, string> scalarsByVariableName)
    {
        var found = FindBareSelects(text, scalarsByVariableName);
        if (found.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length + (found.Count * 16));
        var cursor = 0;
        foreach (var bare in found)
        {
            sb.Append(text, cursor, bare.InsertOffset - cursor);
            sb.Append(" As [").Append(bare.DeclaredName).Append(']');
            cursor = bare.InsertOffset;
        }
        sb.Append(text, cursor, text.Length - cursor);
        return sb.ToString();
    }

    /// <summary>
    /// Every qualifying bare single-scalar select in <paramref name="text"/>, in source order.
    /// </summary>
    public static List<BareSelect> FindBareSelects(string text, IDictionary<string, string> scalarsByVariableName)
    {
        var results = new List<BareSelect>();
        foreach (var columns in EnumerateSelects(text, scalarsByVariableName))
        {
            if (columns.Count != 1) continue;
            var only = columns[0];
            if (only.HasAlias || !only.IsBareVariable) continue;

            results.Add(new BareSelect
            {
                VariableOffset = only.VariableOffset,
                VariableLength = only.VariableLength,
                InsertOffset = only.VariableOffset + only.VariableLength,
                DeclaredName = only.DeclaredName,
            });
        }
        return results;
    }

    /// <summary>
    /// Every select whose top-level column list is 2+ output-scalar references (aliased or not).
    /// A mixed list (a scalar reference plus a real column) is NOT reported — that is a different
    /// failure, covered by SP0031.
    /// </summary>
    public static List<MultiSelect> FindMultiScalarSelects(string text, IDictionary<string, string> scalarsByVariableName)
    {
        var results = new List<MultiSelect>();
        foreach (var columns in EnumerateSelects(text, scalarsByVariableName))
        {
            if (columns.Count < 2) continue;
            var allScalars = true;
            foreach (var c in columns)
                if (!c.IsBareVariable) { allScalars = false; break; }
            if (!allScalars) continue;

            var multi = new MultiSelect { SelectOffset = columns[0].SelectOffset };
            foreach (var c in columns)
                multi.DeclaredNames.Add(c.DeclaredName);
            results.Add(multi);
        }
        return results;
    }

    /// <summary>One parsed entry in a select's top-level column list.</summary>
    private sealed class Column
    {
        public int SelectOffset;
        public int VariableOffset;
        public int VariableLength;
        public string DeclaredName = "";
        /// <summary><c>true</c> when the entry is EXACTLY a declared output-scalar reference
        /// (optionally followed by an <c>As</c> alias) and nothing else.</summary>
        public bool IsBareVariable;
        public bool HasAlias;
    }

    /// <summary>
    /// Walks <paramref name="text"/> and yields the top-level column list of every <c>Select</c>
    /// statement whose list consists solely of comma-separated entries. Comments
    /// (<c>--</c>, <c>/* */</c>), string literals (<c>'…'</c>), quoted identifiers (<c>"…"</c>),
    /// and bracketed identifiers (<c>[…]</c>) are skipped so a <c>Select</c> inside them is never
    /// seen. Any select whose list cannot be resolved to a clean entry sequence yields nothing.
    /// </summary>
    private static IEnumerable<List<Column>> EnumerateSelects(string text, IDictionary<string, string> scalarsByVariableName)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (SkipNonCode(text, ref i))
                continue;

            if (!IsWordAt(text, i, "select"))
            {
                i = SkipWord(text, i);
                continue;
            }

            var selectOffset = i;
            var cursor = i + "select".Length;
            var columns = ParseColumnList(text, cursor, selectOffset, scalarsByVariableName, out var listEnd);
            if (columns is not null)
                yield return columns;

            i = listEnd > selectOffset ? listEnd : selectOffset + "select".Length;
        }
    }

    /// <summary>
    /// Parses the comma-separated top-level column list that starts at <paramref name="start"/>.
    /// Returns <c>null</c> when the list is not a clean entry sequence (e.g. an assignment
    /// <c>Select @X = …</c>, a <c>From</c> clause, a parenthesised expression). On success,
    /// <paramref name="listEnd"/> is the offset just past the list.
    /// </summary>
    private static List<Column>? ParseColumnList(
        string text, int start, int selectOffset,
        IDictionary<string, string> scalarsByVariableName, out int listEnd)
    {
        var columns = new List<Column>();
        var i = start;
        listEnd = start;

        while (true)
        {
            SkipTrivia(text, ref i);
            var column = new Column { SelectOffset = selectOffset };

            // A declared output-scalar reference?
            if (i < text.Length && text[i] == '@')
            {
                var nameStart = i;
                var j = i + 1;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                var variable = text.Substring(nameStart, j - nameStart);
                if (scalarsByVariableName.TryGetValue(variable.ToLowerInvariant(), out var declared))
                {
                    column.VariableOffset = nameStart;
                    column.VariableLength = j - nameStart;
                    column.DeclaredName = declared;
                    column.IsBareVariable = true;
                    i = j;
                }
                else
                {
                    // An @variable that is not a declared output scalar: the statement is not ours.
                    listEnd = i;
                    return null;
                }
            }
            else
            {
                // Not a scalar reference. Consume one identifier-ish token so a MIXED list is still
                // recognisable as a list (FindMultiScalarSelects rejects it via IsBareVariable).
                var j = i;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '.')) j++;
                if (j == i) { listEnd = i; return null; }   // punctuation/operator → not a plain list
                i = j;
            }

            SkipTrivia(text, ref i);

            // Optional `As <alias>`.
            if (IsWordAt(text, i, "as"))
            {
                column.HasAlias = true;
                i += 2;
                SkipTrivia(text, ref i);
                var j = i;
                if (j < text.Length && text[j] == '[')
                {
                    while (j < text.Length && text[j] != ']') j++;
                    if (j < text.Length) j++;
                }
                else
                {
                    while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                }
                if (j == i) { listEnd = i; return null; }
                i = j;
                SkipTrivia(text, ref i);
            }

            columns.Add(column);

            if (i < text.Length && text[i] == ',')
            {
                i++;
                continue;   // another entry
            }

            // End of the list. It only counts when the next significant token terminates the
            // statement — otherwise the last entry was part of a larger expression.
            listEnd = i;
            if (i >= text.Length) return columns;                  // end of text
            if (text[i] == ';') return columns;                    // explicit terminator
            var word = PeekWord(text, i);
            if (word.Length > 0 && StatementStarters.Contains(word)) return columns;
            return null;                                            // `From`, an operator, `(`, `.` …
        }
    }

    /// <summary>
    /// Skips whitespace and comments. Used between tokens inside a column list. T-SQL
    /// <c>/* */</c> comments NEST (unlike ANSI SQL), so a block comment is depth-tracked —
    /// an inner <c>/*</c> increments depth, a <c>*/</c> decrements it, and only a <c>*/</c> at
    /// depth 0 actually closes the comment. Everything between the opening <c>/*</c> and the
    /// matching close (including quotes) is consumed as comment text, never re-entering the
    /// quote/bracket handling in <see cref="SkipNonCode"/>. An unterminated comment simply runs
    /// to end-of-text (no infinite loop).
    /// </summary>
    private static void SkipTrivia(string text, ref int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (text[i] == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var depth = 1;
                i += 2;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }
                continue;
            }
            return;
        }
    }

    /// <summary>
    /// Skips one span of non-code at <paramref name="i"/> — whitespace, a comment, a string
    /// literal, a quoted identifier, or a bracketed identifier. Returns <c>true</c> when
    /// <paramref name="i"/> advanced.
    /// </summary>
    private static bool SkipNonCode(string text, ref int i)
    {
        var before = i;
        SkipTrivia(text, ref i);
        if (i < text.Length && (text[i] == '\'' || text[i] == '"'))
        {
            var quote = text[i];
            i++;
            while (i < text.Length)
            {
                if (text[i] == quote)
                {
                    // Doubled quote is an escape inside the literal.
                    if (i + 1 < text.Length && text[i + 1] == quote) { i += 2; continue; }
                    i++;
                    break;
                }
                i++;
            }
        }
        else if (i < text.Length && text[i] == '[')
        {
            while (i < text.Length && text[i] != ']') i++;
            if (i < text.Length) i++;
        }
        return i != before;
    }

    /// <summary>Advances past one word (or one character when the position is punctuation).</summary>
    private static int SkipWord(string text, int i)
    {
        if (i >= text.Length) return i + 1;
        if (!char.IsLetter(text[i]) && text[i] != '_' && text[i] != '@') return i + 1;
        var j = i;
        if (text[j] == '@') j++;
        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
        return j > i ? j : i + 1;
    }

    /// <summary>
    /// <c>true</c> when <paramref name="word"/> sits at <paramref name="i"/> as a whole word
    /// (case-insensitive, not preceded or followed by an identifier character).
    /// </summary>
    private static bool IsWordAt(string text, int i, string word)
    {
        if (i < 0 || i + word.Length > text.Length) return false;
        if (string.Compare(text, i, word, 0, word.Length, System.StringComparison.OrdinalIgnoreCase) != 0)
            return false;
        if (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_' || text[i - 1] == '@'))
            return false;
        var after = i + word.Length;
        if (after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_'))
            return false;
        return true;
    }

    /// <summary>The identifier word at <paramref name="i"/>, or an empty string.</summary>
    private static string PeekWord(string text, int i)
    {
        if (i >= text.Length || (!char.IsLetter(text[i]) && text[i] != '_')) return "";
        var j = i;
        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
        return text.Substring(i, j - i);
    }
}
