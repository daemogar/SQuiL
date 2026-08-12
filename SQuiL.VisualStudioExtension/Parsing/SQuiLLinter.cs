using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SQuiL.VisualStudioExtension.Parsing;

/// <summary>
/// Secondary lint passes that aren't part of <see cref="SQuiLParser"/>'s
/// core parse — port of the <c>lintVariableNames</c> and
/// <c>lintStatementTerminators</c> methods in
/// <c>SQuiL.VSCodeExtension/src/providers/diagnosticsProvider.ts</c>.
///
/// Kept separate from the parser so they can be re-run cheaply without
/// re-parsing, and so a future consumer (e.g. a CLI lint command) can opt in
/// to just the parse without these stylistic suggestions.
/// </summary>
internal static class SQuiLLinter
{
    private static readonly (Regex Pattern, string Correct)[] TypoPatterns =
    {
        (new Regex(@"@param_",   RegexOptions.Compiled | RegexOptions.IgnoreCase), "@Param_"),
        (new Regex(@"@params_",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "@Params_"),
        (new Regex(@"@return_",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "@Return_"),
        (new Regex(@"@returns_", RegexOptions.Compiled | RegexOptions.IgnoreCase), "@Returns_"),
    };

    private static readonly Regex DeclarePrefix = new(
        @"^\s*DECLARE\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BlockCommentEnd = new(
        @"\*/$",
        RegexOptions.Compiled);

    private static readonly Regex TableOpenWithoutClose = new(
        @"TABLE\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Append "Prefer 'Param_'" suggestions and "DECLARE missing ;" hints to
    /// <paramref name="diagnostics"/>.  Severity for both is <c>Info</c> — these
    /// are style hints, not errors.  Also runs the undeclared-variable /
    /// special-placement validation (errors/warnings).
    /// </summary>
    /// <param name="text">Full text of the .squil file.</param>
    /// <param name="diagnostics">Diagnostic list to append to.</param>
    /// <param name="squilFilePath">
    /// Absolute path to the .squil file on disk.  When supplied the linter also
    /// runs the context-resolver pass (SP0028 orphan / SP0027 duplicate mirror).
    /// Pass <c>null</c> when path is unavailable (e.g. untitled buffers).
    /// </param>
    /// <param name="dialect">
    /// The resolved editor dialect for the owning project (SQL Server vs SQLite).  Threaded
    /// into every re-parse below so SQLite <c>Create Temp Table</c> declarations are recognized
    /// (and the SQL-Server-only "missing USE" warning does not fire), and so SP0040's severity
    /// follows the dialect.  Mirrors <c>parseSQuiL(text, dialect)</c> in parser.ts (VS Code),
    /// which parses once with the dialect and runs every lint on that result.
    /// </param>
    public static void Lint(string text, List<SQuiLDiagnostic> diagnostics, string? squilFilePath = null, EditorDialect dialect = EditorDialect.SqlServer)
    {
        string[] lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (DeclarePrefix.IsMatch(line))
            {
                LintCasing(line, i, diagnostics);
                LintMissingSemicolon(line, i, diagnostics);
            }
        }

        LintUndeclaredVariables(text, diagnostics);
        LintNullabilityHints(text, diagnostics, dialect);
        LintShapeMismatch(text, diagnostics, dialect);
        LintShapeCollision(text, diagnostics, dialect);
        LintSimilarSignatures(text, diagnostics, dialect);
        LintCardinalityCollision(text, diagnostics, dialect);
        LintUnmatchedSelect(text, diagnostics, dialect);
        LintMultiScalarSelect(text, diagnostics, dialect);
        LintScalarAliasHint(text, diagnostics, dialect);
        LintTimestampInput(text, diagnostics, dialect);
        LintScalarNullMarker(text, diagnostics, dialect);
        LintKeyGraph(text, diagnostics, dialect);
        LintParamsBeforeReturns(text, diagnostics, dialect);
        if (squilFilePath is not null)
        {
            LintOrphanContext(squilFilePath, diagnostics);
            LintMutationDiagnostics(text, squilFilePath, diagnostics, dialect);
            LintDebugRollbackHint(text, squilFilePath, diagnostics, dialect);
        }
    }

    // ── Params-before-returns ordering (SP0040) ──────────────────────────────
    //
    // Within one file, every @Param/@Params (input) must be declared before any
    // @Return/@Returns (output). Reported once, anchored at the first offending
    // output (the earliest output still followed by a later input). Severity is
    // dialect-dependent: Error for every temp-table-header dialect (SQLite, PostgreSQL —
    // their Create-Temp-Table header must create the input tables before the shred reads
    // them), Warning otherwise.
    //
    // Port of SQuiLOrderingValidator.cs (source generator) and
    // lintParamsBeforeReturns() in parser.ts (VS Code) — change one, change all three.
    //
    // Generalized (Task 8) from a SQLite-only gate to the full temp-table family via
    // SQuiLDialect.IsTempTableDialect(), matching the generator's FileGenerator.cs, which
    // now gates on `dialect is ITempTableHeaderDialect`.

    internal static void LintParamsBeforeReturns(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);

        static bool IsInput(VariableRole r) => r == VariableRole.Param || r == VariableRole.Params || r == VariableRole.ParamTable;
        static bool IsOutput(VariableRole r) => r == VariableRole.Return || r == VariableRole.Returns || r == VariableRole.ReturnTable;

        // Only INPUT/OUTPUT declarations participate, in file order. Specials/unknowns are skipped.
        var decls = parsed.Variables.Where(v => IsInput(v.Role) || IsOutput(v.Role)).ToList();

        // Index of the last input; any output before it is out of order. No inputs → cannot violate.
        int lastInputIndex = -1;
        for (int i = 0; i < decls.Count; i++)
            if (IsInput(decls[i].Role)) lastInputIndex = i;
        if (lastInputIndex < 0) return;

        for (int i = 0; i < lastInputIndex; i++)
        {
            var v = decls[i];
            if (!IsOutput(v.Role)) continue;
            diagnostics.Add(new SQuiLDiagnostic
            {
                Message   = $"`{v.RawName}` (an output) is declared before a later @Param/@Params input. " +
                            "Declare all @Param/@Params (inputs) before any @Return/@Returns (outputs).",
                Line      = v.Line,
                StartChar = v.Character,
                EndChar   = v.Character + v.RawName.Length,
                Severity  = SQuiLDialect.IsTempTableDialect(dialect) ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                Code      = "SP0040",
            });
            return;
        }
    }

    // ── SP0041 / SP0042 shared scanner: implicit scalar select alias ────────
    //
    // Port of ScalarSelectAliaser.cs (source generator) — change one, change all four
    // (this file, the Visual Studio copy, the source generator, and parser.ts / VS Code).
    //
    // A bare `Select @Return_X;` returns an UNNAMED column, so the runtime shape-key
    // router can't match it (SQL Server only — SQLite/PostgreSQL declare scalars as
    // single-column temp tables and select a real named column). FindBareScalarSelects
    // locates every qualifying bare single-scalar select (consumed by LintScalarAliasHint,
    // SP0042, below); FindMultiScalarSelects locates every select whose top-level column
    // list is 2+ output-scalar references, which can never be routed regardless of
    // aliasing (consumed by LintMultiScalarSelect, SP0041, below). Both walk the same
    // underlying scanner (EnumerateScalarSelects), which skips comments (`--`, NESTED
    // `/* */` — T-SQL block comments nest, unlike ANSI SQL), string literals, quoted
    // identifiers, and bracketed identifiers exactly like ScalarSelectAliaser.cs.

    /// <summary>One parsed entry in a select's top-level column list — scanner-internal,
    /// never returned to a caller. Mirrors ScalarSelectAliaser.cs's private Column.</summary>
    private sealed class ScalarSelectColumn
    {
        public int SelectOffset;
        public int VariableOffset;
        public int VariableLength;
        public string DeclaredName = "";
        /// <summary><c>true</c> when the entry is EXACTLY a declared output-scalar
        /// reference (optionally followed by an <c>As</c> alias) and nothing else.</summary>
        public bool IsBareVariable;
        public bool HasAlias;
    }

    /// <summary>A bare single-scalar select that qualifies for an implicit alias (SP0042).</summary>
    internal sealed class BareScalarSelect
    {
        /// <summary>Offset of the <c>@Return_X</c> token.</summary>
        public int VariableOffset;
        /// <summary>Length of the <c>@Return_X</c> token.</summary>
        public int VariableLength;
        /// <summary>The declared base name, in its declared casing.</summary>
        public string DeclaredName = "";
    }

    /// <summary>A select whose top-level column list is 2+ output-scalar references (SP0041).</summary>
    internal sealed class MultiScalarSelect
    {
        /// <summary>Offset of the <c>Select</c> keyword.</summary>
        public int SelectOffset;
        /// <summary>The declared base names referenced, in source order.</summary>
        public List<string> DeclaredNames = new();
    }

    /// <summary>Port of ScalarSelectAliaser.cs's StatementStarters — change one, change all four.</summary>
    private static readonly HashSet<string> ScalarSelectStatementStarters = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "select", "insert", "update", "delete", "set", "declare", "if", "while", "begin", "end",
        "exec", "execute", "return", "print", "use", "with", "merge", "truncate", "drop", "create",
        "alter", "go", "else", "commit", "rollback", "throw", "raiserror", "waitfor",
    };

    /// <summary>Maps a lower-cased <c>"@return_&lt;name&gt;"</c> key to its declared base
    /// name, for every declared output-scalar (<see cref="VariableRole.Return"/>) variable.
    /// Shared by <see cref="LintMultiScalarSelect"/> (SP0041) and
    /// <see cref="LintScalarAliasHint"/> (SP0042), and (as <c>internal</c>) by
    /// <c>SQuiLSuggestedActionsSource.ComputeEdits</c> to resolve the SP0042 quick-fix's
    /// declared name.</summary>
    internal static Dictionary<string, string> BuildScalarsByVariableName(List<SQuiLVariable> variables)
    {
        var map = new Dictionary<string, string>();
        foreach (var v in variables)
            if (v.Role == VariableRole.Return)
                map[$"@return_{v.Name}".ToLowerInvariant()] = v.Name;
        return map;
    }

    /// <summary>
    /// Port of ScalarSelectAliaser.cs's FindBareSelects — change one, change all four.
    /// Every qualifying bare single-scalar select in <paramref name="text"/>, in source
    /// order.
    /// </summary>
    internal static List<BareScalarSelect> FindBareScalarSelects(string text, Dictionary<string, string> scalarsByVariableName)
    {
        var results = new List<BareScalarSelect>();
        foreach (var columns in EnumerateScalarSelects(text, scalarsByVariableName))
        {
            if (columns.Count != 1) continue;
            var only = columns[0];
            if (only.HasAlias || !only.IsBareVariable) continue;

            results.Add(new BareScalarSelect
            {
                VariableOffset = only.VariableOffset,
                VariableLength = only.VariableLength,
                DeclaredName = only.DeclaredName,
            });
        }
        return results;
    }

    /// <summary>
    /// Port of ScalarSelectAliaser.cs's FindMultiScalarSelects — change one, change all
    /// four. Every select whose top-level column list is 2+ output-scalar references
    /// (aliased or not), in source order. A mixed list (a scalar reference plus a real
    /// column) is NOT reported — that is SP0031's domain.
    /// </summary>
    internal static List<MultiScalarSelect> FindMultiScalarSelects(string text, Dictionary<string, string> scalarsByVariableName)
    {
        var results = new List<MultiScalarSelect>();
        foreach (var columns in EnumerateScalarSelects(text, scalarsByVariableName))
        {
            if (columns.Count < 2) continue;
            var allScalars = true;
            foreach (var c in columns)
                if (!c.IsBareVariable) { allScalars = false; break; }
            if (!allScalars) continue;

            var multi = new MultiScalarSelect { SelectOffset = columns[0].SelectOffset };
            foreach (var c in columns)
                multi.DeclaredNames.Add(c.DeclaredName);
            results.Add(multi);
        }
        return results;
    }

    /// <summary>
    /// Port of ScalarSelectAliaser.cs's EnumerateSelects — change one, change all four.
    /// Walks <paramref name="text"/> and yields the top-level column list of every
    /// <c>Select</c> statement whose list consists solely of comma-separated entries.
    /// Comments (<c>--</c>, <c>/* */</c>), string literals (<c>'…'</c>), quoted
    /// identifiers (<c>"…"</c>), and bracketed identifiers (<c>[…]</c>) are skipped so a
    /// <c>Select</c> inside them is never seen. Any select whose list cannot be resolved
    /// to a clean entry sequence yields nothing.
    /// </summary>
    private static IEnumerable<List<ScalarSelectColumn>> EnumerateScalarSelects(
        string text, Dictionary<string, string> scalarsByVariableName)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (SkipScalarNonCode(text, ref i))
                continue;

            if (!IsScalarWordAt(text, i, "select"))
            {
                i = SkipScalarWord(text, i);
                continue;
            }

            var selectOffset = i;
            var cursor = i + "select".Length;
            var columns = ParseScalarColumnList(text, cursor, selectOffset, scalarsByVariableName, out var listEnd);
            if (columns is not null)
                yield return columns;

            i = listEnd > selectOffset ? listEnd : selectOffset + "select".Length;
        }
    }

    /// <summary>
    /// Port of ScalarSelectAliaser.cs's ParseColumnList — change one, change all four.
    /// Parses the comma-separated top-level column list that starts at
    /// <paramref name="start"/>. Returns <c>null</c> when the list is not a clean entry
    /// sequence (e.g. an assignment <c>Select @X = …</c>, a <c>From</c> clause, a
    /// parenthesised expression). On success, <paramref name="listEnd"/> is the offset
    /// just past the list.
    /// </summary>
    private static List<ScalarSelectColumn>? ParseScalarColumnList(
        string text, int start, int selectOffset,
        Dictionary<string, string> scalarsByVariableName, out int listEnd)
    {
        var columns = new List<ScalarSelectColumn>();
        var i = start;
        listEnd = start;

        while (true)
        {
            SkipScalarTrivia(text, ref i);
            var column = new ScalarSelectColumn { SelectOffset = selectOffset };

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
                // Not a scalar reference. Consume one identifier-ish token so a MIXED list is
                // still recognisable as a list (FindMultiScalarSelects rejects it via IsBareVariable).
                var j = i;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '.')) j++;
                if (j == i) { listEnd = i; return null; }   // punctuation/operator → not a plain list
                i = j;
            }

            SkipScalarTrivia(text, ref i);

            // Optional `As <alias>`.
            if (IsScalarWordAt(text, i, "as"))
            {
                column.HasAlias = true;
                i += 2;
                SkipScalarTrivia(text, ref i);
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
                SkipScalarTrivia(text, ref i);
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
            var word = PeekScalarWord(text, i);
            if (word.Length > 0 && ScalarSelectStatementStarters.Contains(word)) return columns;
            return null;                                            // `From`, an operator, `(`, `.` …
        }
    }

    /// <summary>
    /// Port of ScalarSelectAliaser.cs's SkipTrivia — change one, change all four. Skips
    /// whitespace and comments. T-SQL <c>/* */</c> comments NEST (unlike ANSI SQL), so a
    /// block comment is depth-tracked — an inner <c>/*</c> increments depth, a <c>*/</c>
    /// decrements it, and only a <c>*/</c> at depth 0 actually closes the comment. An
    /// unterminated comment simply runs to end-of-text (no infinite loop).
    /// </summary>
    private static void SkipScalarTrivia(string text, ref int i)
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
    /// Port of ScalarSelectAliaser.cs's SkipNonCode — change one, change all four. Skips
    /// one span of non-code at <paramref name="i"/> — whitespace, a comment, a string
    /// literal, a quoted identifier, or a bracketed identifier. Returns <c>true</c> when
    /// <paramref name="i"/> advanced.
    /// </summary>
    private static bool SkipScalarNonCode(string text, ref int i)
    {
        var before = i;
        SkipScalarTrivia(text, ref i);
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

    /// <summary>Port of ScalarSelectAliaser.cs's SkipWord — change one, change all four.
    /// Advances past one word (or one character when the position is punctuation).</summary>
    private static int SkipScalarWord(string text, int i)
    {
        if (i >= text.Length) return i + 1;
        if (!char.IsLetter(text[i]) && text[i] != '_' && text[i] != '@') return i + 1;
        var j = i;
        if (text[j] == '@') j++;
        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
        return j > i ? j : i + 1;
    }

    /// <summary>
    /// Port of ScalarSelectAliaser.cs's IsWordAt — change one, change all four.
    /// <c>true</c> when <paramref name="word"/> sits at <paramref name="i"/> as a whole
    /// word (case-insensitive, not preceded or followed by an identifier character).
    /// </summary>
    private static bool IsScalarWordAt(string text, int i, string word)
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

    /// <summary>Port of ScalarSelectAliaser.cs's PeekWord — change one, change all four.
    /// The identifier word at <paramref name="i"/>, or an empty string.</summary>
    private static string PeekScalarWord(string text, int i)
    {
        if (i >= text.Length || (!char.IsLetter(text[i]) && text[i] != '_')) return "";
        var j = i;
        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
        return text.Substring(i, j - i);
    }

    // ── SP0041: multi-scalar select detection ────────────────────────────────
    //
    // A Select whose top-level column list is 2+ declared output-scalar references
    // cannot be routed to a response — only one scalar per Select is routable (splitting
    // the select into one-per-scalar is the fix). Runs on every dialect: the scan
    // naturally finds nothing on a temp-table-header dialect, since those authors
    // reference real temp-table columns, never `@`-prefixed scalars, in the query body.
    //
    // Port of SQuiLMultiScalarSelectValidator.cs (source generator) and
    // lintMultiScalarSelect() in parser.ts (VS Code) — change one, change all three.

    internal static void LintMultiScalarSelect(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);
        var scalarsByVariableName = BuildScalarsByVariableName(parsed.Variables);
        if (scalarsByVariableName.Count == 0) return;

        foreach (var multi in FindMultiScalarSelects(sql, scalarsByVariableName))
        {
            var (line, startChar) = OffsetToLineChar(sql, multi.SelectOffset);
            var endChar = startChar + "select".Length;
            var names = multi.DeclaredNames;

            diagnostics.Add(new SQuiLDiagnostic
            {
                Message   = $"This Select returns more than one output scalar ({string.Join(", ", names)}), " +
                            "which cannot be routed to a response. Use one Select per scalar.",
                Line      = line,
                StartChar = startChar,
                EndChar   = endChar,
                Severity  = DiagnosticSeverity.Error,
                Code      = "SP0041",
            });
        }
    }

    // ── SP0042: implicit scalar select alias hint ────────────────────────────
    //
    // EDITOR-ONLY — must NOT appear in the source generator. Port of
    // ScalarSelectAliaser.cs's FindBareSelects (source generator) and
    // scalarAliasHints.ts (VS Code) — change one, change all four.
    //
    // The generator itself auto-appends `As [<Name>]` to a qualifying bare scalar select
    // (SQL Server only — see ScalarSelectAliaser.Rewrite); this hint surfaces that silent
    // rewrite to the author so they can write the alias themselves. Returns immediately
    // for a temp-table-header dialect (SQLite, PostgreSQL): those declare scalars as
    // single-column temp tables and select a real named column, so there is nothing to
    // alias.

    internal static void LintScalarAliasHint(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        if (SQuiLDialect.IsTempTableDialect(dialect)) return;

        var parsed = SQuiLParser.Parse(sql, dialect);
        var scalarsByVariableName = BuildScalarsByVariableName(parsed.Variables);
        if (scalarsByVariableName.Count == 0) return;

        foreach (var bare in FindBareScalarSelects(sql, scalarsByVariableName))
        {
            var (line, startChar) = OffsetToLineChar(sql, bare.VariableOffset);
            var declaredName = bare.DeclaredName;

            diagnostics.Add(new SQuiLDiagnostic
            {
                Message   = $"The generator supplies `As [{declaredName}]`; add it to make the column name explicit.",
                Line      = line,
                StartChar = startChar,
                EndChar   = startChar + bare.VariableLength,
                Severity  = DiagnosticSeverity.Info,
                Code      = "SP0042",
            });
        }
    }

    // ── SP0031: unmatched standalone SELECT (editor-only warning) ────────────
    //
    // Best-effort, name-focused. Fires when a standalone `Select <col-list> From …`
    // in the query body produces a column-name sequence that matches no declared
    // @Return_/@Returns_ output signature. Ignores `Select *`, `Insert Into … Select …`,
    // and any SELECT whose columns can't be statically resolved to names (bail on
    // un-aliased expressions — best-effort).
    //
    // EDITOR-ONLY — must NOT appear in the source generator.
    //
    // Port of lintUnmatchedSelect() in parser.ts (VS Code extension) —
    // change one side, change all three.

    // ^\s*select\s+ anchor already excludes Insert Into … Select … and Set … lines
    private static readonly Regex SelectFromRegex = new(
        @"^\s*select\s+(?!\*)(.+?)\s+from\s",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Task 6 scalar extension: a bare `Select <expr>[;]` line (no FROM required), and the
    // leading `@variable` + remainder split used to test whether that expression is a
    // resolvable alias of a declared output scalar. Port of the `mBare`/`scalarMatch`
    // regexes in lintUnmatchedSelect() in parser.ts (VS Code) — change one, change all three.
    private static readonly Regex SelectBareRegex = new(
        @"^\s*select\s+(?!\*)(.+?)\s*;?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ScalarSelectExprRegex = new(
        @"^\s*(@[A-Za-z_][A-Za-z0-9_]*)\s*(.*)$",
        RegexOptions.Compiled);

    // The alias, if bracketed (`As [Count]`), is unwrapped here just like ExtractSelectColumnNames's
    // `\[?...\]?` groups below — an author-written bracketed alias (or the generator's own implicit
    // one) still matches the declared name.
    private static readonly Regex ScalarSelectAliasRegex = new(
        @"^as\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static void LintUnmatchedSelect(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);

        var outputs = parsed.Variables
            .Where(v => (v.Role == VariableRole.Returns || v.Role == VariableRole.ReturnTable)
                        && v.Columns != null && v.Columns.Count > 0)
            .ToList();
        // Task 6 scalar extension: declared output-scalar (@Return_) variables participate too —
        // a scalar select's ALIAS is checked against its declared name (a BARE reference and the
        // assignment form are left alone; see below).
        var scalars = parsed.Variables.Where(v => v.Role == VariableRole.Return).ToList();

        if (outputs.Count == 0 && scalars.Count == 0) return;

        // Build the set of declared output column-name sequences (lower-cased). Each declared
        // output scalar's own name is a single-entry key too.
        var declaredNameKeys = new HashSet<string>(
            outputs.Select(v => string.Join("|", v.Columns!.Select(c => c.Name.ToLowerInvariant()))));
        var scalarsByVariableName = BuildScalarsByVariableName(parsed.Variables);
        foreach (var v in scalars) declaredNameKeys.Add(v.Name.ToLowerInvariant());

        // Determine body start: everything after the USE statement line.
        if (parsed.DatabaseLine is not { } databaseLine) return;

        var allLines = sql.Split('\n');
        int bodyLineOffset = databaseLine + 1;

        string expected = string.Join(" | ", outputs
            .Select(v => string.Join(", ", v.Columns!.Select(c => c.Name)))
            .Concat(scalars.Select(v => v.Name)));

        for (int i = 0; i < allLines.Length - bodyLineOffset; i++)
        {
            string raw = allLines[bodyLineOffset + i];

            // Table case (unchanged): ^\s*select\s+ anchor already excludes Insert Into …
            // Select … and Set … lines. Gated on outputs.Count > 0 — a file whose only
            // declared output is a scalar has no table declaredNameKeys entries, so this
            // branch can never match and must not run (previously fired a false-positive
            // SP0031 on every multi-column SELECT).
            var selectMatch = outputs.Count > 0 ? SelectFromRegex.Match(raw) : Match.Empty;
            if (selectMatch.Success)
            {
                var cols = ExtractSelectColumnNames(selectMatch.Groups[1].Value);
                if (cols == null) continue;                     // not statically inferable -> skip

                string key = string.Join("|", cols.Select(c => c.ToLowerInvariant()));
                if (declaredNameKeys.Contains(key)) continue;

                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message   = $"This SELECT's columns ({string.Join(", ", cols)}) match no declared @Returns_/@Return_ output signature. " +
                                $"Expected one of: {expected}. " +
                                "Add AS aliases (and CAST base types) to match, or use Insert Into @Returns_X … ; Select * From @Returns_X;.",
                    Line      = bodyLineOffset + i,
                    StartChar = 0,
                    EndChar   = raw.Length,
                    Severity  = DiagnosticSeverity.Warning,
                    Code      = "SP0031",
                });
                continue;
            }

            // Scalar case (the Task 6 extension): the FROM requirement is relaxed to also
            // consider a bare `Select <expr>[;]` line, but only a RESOLVABLE alias that
            // mismatches the declared name fires. A bare reference (no alias — SP0042's
            // territory) and the assignment form (`Select @X = …`) are left alone.
            if (scalars.Count == 0) continue;
            var bareMatch = SelectBareRegex.Match(raw);
            if (!bareMatch.Success) continue;
            var scalarMatch = ScalarSelectExprRegex.Match(bareMatch.Groups[1].Value);
            if (!scalarMatch.Success) continue;
            if (!scalarsByVariableName.TryGetValue(scalarMatch.Groups[1].Value.ToLowerInvariant(), out var declaredName))
                continue;                                       // not a declared output scalar reference

            string rest = scalarMatch.Groups[2].Value.Trim();
            if (rest.Length == 0) continue;                     // bare — SP0042's territory
            if (rest.StartsWith("=")) continue;                 // assignment form

            var aliasMatch = ScalarSelectAliasRegex.Match(rest);
            if (!aliasMatch.Success) continue;                  // not a resolvable alias -> bail (best-effort)
            string alias = aliasMatch.Groups[1].Value;
            if (string.Equals(alias, declaredName, System.StringComparison.OrdinalIgnoreCase))
                continue;                                       // correctly aliased

            diagnostics.Add(new SQuiLDiagnostic
            {
                Message   = $"This SELECT's columns ({alias}) match no declared @Returns_/@Return_ output signature. " +
                            $"Expected one of: {expected}. " +
                            "Add AS aliases (and CAST base types) to match, or use Insert Into @Returns_X … ; Select * From @Returns_X;.",
                Line      = bodyLineOffset + i,
                StartChar = 0,
                EndChar   = raw.Length,
                Severity  = DiagnosticSeverity.Warning,
                Code      = "SP0031",
            });
        }
    }

    /// <summary>
    /// Best-effort: returns output column names for a simple comma list, or
    /// <c>null</c> if not statically inferable (un-aliased expression).
    /// Port of <c>extractSelectColumnNames</c> in parser.ts.
    /// </summary>
    private static List<string>? ExtractSelectColumnNames(string list)
    {
        var parts = SplitTopLevelCommas(list);
        var names = new List<string>();
        foreach (var p in parts)
        {
            // AS alias takes precedence.
            var asMatch = Regex.Match(p, @"\s+as\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$",
                RegexOptions.IgnoreCase);
            if (asMatch.Success) { names.Add(asMatch.Groups[1].Value); continue; }

            // table.column or bare column identifier.
            var dottedMatch = Regex.Match(p, @"\.\s*\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$");
            if (dottedMatch.Success) { names.Add(dottedMatch.Groups[1].Value); continue; }

            var bareMatch = Regex.Match(p, @"^\s*\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$");
            if (bareMatch.Success) { names.Add(bareMatch.Groups[1].Value); continue; }

            return null;   // un-aliased expression -> can't infer a column name -> bail
        }
        return names;
    }

    /// <summary>
    /// Splits <paramref name="str"/> on top-level commas (not inside parentheses).
    /// Port of <c>splitTopLevelCommas</c> in parser.ts.
    /// </summary>
    private static List<string> SplitTopLevelCommas(string str)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < str.Length; i++)
        {
            if (str[i] == '(') depth++;
            else if (str[i] == ')') depth--;
            else if (str[i] == ',' && depth == 0)
            {
                parts.Add(str.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(str.Substring(start));
        return parts;
    }

    // ── Timestamp-input detection (SP0032) ───────────────────────────────────
    //
    // timestamp/rowversion is a server-generated, read-only value and cannot be
    // a meaningful input. Flags any INPUT declaration (scalar @Param_/@Params_
    // or a column of an input table) whose SQL type is timestamp/rowversion.
    // Output declarations are fine (byte[]).
    //
    // Port of SQuiLTimestampInputValidator.cs (source generator) — change one,
    // change all three (+ TS).

    internal static void LintTimestampInput(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);

        static bool IsTimestamp(string sqlType)
        {
            var t = sqlType.Trim();
            int paren = t.IndexOf('(');
            if (paren >= 0) t = t.Substring(0, paren);
            var word = t.Split(' ')[0];
            return string.Equals(word, "timestamp", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "rowversion", System.StringComparison.OrdinalIgnoreCase);
        }

        foreach (var v in parsed.Variables)
        {
            bool isInput = v.Role == VariableRole.Param || v.Role == VariableRole.Params || v.Role == VariableRole.ParamTable;
            if (!isInput) continue;

            if (v.Columns is { Count: > 0 })
            {
                foreach (var col in v.Columns)
                {
                    if (!IsTimestamp(col.SqlType)) continue;
                    diagnostics.Add(new SQuiLDiagnostic
                    {
                        Message   = $"`{v.Name}.{col.Name}` is a timestamp/rowversion used as an input. " +
                                    "timestamp is server-generated and read-only — use it only on @Return_/@Returns_ outputs, or remove it.",
                        Line      = col.Line,
                        StartChar = col.Character,
                        EndChar   = col.Character + col.Name.Length,
                        Severity  = DiagnosticSeverity.Error,
                        Code      = "SP0032",
                    });
                }
            }
            else if (IsTimestamp(v.SqlType))
            {
                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message   = $"`{v.Name}` is a timestamp/rowversion used as an input. " +
                                "timestamp is server-generated and read-only — use it only on @Return_/@Returns_ outputs, or remove it.",
                    Line      = v.Line,
                    StartChar = v.Character,
                    EndChar   = v.Character + v.RawName.Length,
                    Severity  = DiagnosticSeverity.Error,
                    Code      = "SP0032",
                });
            }
        }
    }

    // ── Scalar standalone null/not null marker detection (SP0037) ────────────
    //
    // A standalone `null`/`not null` marker on a scalar Declare is invalid T-SQL —
    // Declare doesn't support nullability modifiers. Use an `= null` initializer
    // to make the scalar nullable instead. Table/object column markers are
    // unaffected (out of scope).
    //
    // Port of SQuiLScalarMarkerValidator.cs (source generator) — change one,
    // change all three (+ TS).

    internal static void LintScalarNullMarker(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);

        foreach (var v in parsed.Variables)
        {
            if (v.NullabilityMarker is null) continue;

            int startChar = v.NullabilityMarkerCharacter ?? v.Character;
            int length = v.NullabilityMarkerLength ?? v.RawName.Length;

            diagnostics.Add(new SQuiLDiagnostic
            {
                Message   = $"`{v.RawName}` has a `null`/`not null` marker, which is invalid T-SQL on a scalar Declare. " +
                            "Use `= null` to make it nullable, or remove the marker for non-nullable.",
                Line      = v.Line,
                StartChar = startChar,
                EndChar   = startChar + length,
                Severity  = DiagnosticSeverity.Error,
                Code      = "SP0037",
            });
        }
    }

    // ── Nested-objects key-graph diagnostics (SP0033 / SP0034 / SP0035 / SP0036) ──
    //
    // SP0033 (Error): a child table/object's column matches the declared Primary
    //   Key of more than one other table/object (ambiguous parent — a
    //   nested-object child must resolve to exactly one parent).
    // SP0034 (Error): following Primary-Key/Foreign-Key links from a table
    //   eventually returns to that same table (cycle — nested objects require
    //   a tree).
    // SP0035 (Info, editor-only — NOT a build/generator diagnostic): a
    //   table/object's Primary Key that NO other table/object links to, but
    //   ONLY surfaced when nesting is already in play elsewhere in the file
    //   (at least one real parent/child link exists). A deliberately-flat file
    //   whose tables happen to each declare an unrelated Primary Key must NOT
    //   be nagged.
    // SP0036 (Error): a nested-INPUT link column's declared type is neither
    //   integer-family (int/bigint/smallint) nor uniqueidentifier, so the
    //   generator cannot synthesize a join key for it.
    //
    // TWO independent universes participate, never mixed — OUTPUT
    // (@Return_/@Returns_) and INPUT (@Param_/@Params_) table/object variables
    // each get their OWN graph, matching the generator, which calls
    // SQuiLKeyGraph.Build once for OUTPUT blocks and once for INPUT blocks
    // (FileGenerator.cs's keyGraph / inputGraph). SP0033/SP0034/SP0035 apply to
    // BOTH graphs; SP0036 applies to the INPUT graph only (OUTPUT never
    // synthesizes keys).
    //
    // Mirrors SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs (SP0033/SP0034
    // are also build-time errors there; SP0036 mirrors FileGenerator.cs's
    // IsSynthesizableKeyType/ReportUnsupportedKeyType) and keyGraph.ts /
    // nestedObjectHints.ts (VS Code extension) — change one side, change all three.

    // ── Shared key-graph builder ─────────────────────────────────────────────
    //
    // Parent/child resolution shared between the SP0033/SP0034/SP0035
    // diagnostics below and the nested-object hover role text
    // (SQuiLQuickInfoSource.cs's DescribeColumnLinkRole) — one algorithm, not
    // a third duplicated copy. Mirrors `buildKeyGraph` in keyGraph.ts (VS Code)
    // and SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs (generator).

    internal sealed class KeyGraphEdge
    {
        public SQuiLVariable Parent { get; set; } = null!;
        public SQuiLVariable Child { get; set; } = null!;
        public string KeyName { get; set; } = "";
    }

    internal sealed class KeyGraphAmbiguity
    {
        public SQuiLVariable Child { get; set; } = null!;
        public SQuiLVariable OtherParent { get; set; } = null!;
    }

    internal sealed class KeyGraph
    {
        public List<KeyGraphEdge> Edges { get; } = new();
        public List<KeyGraphAmbiguity> Ambiguities { get; } = new();
        public Dictionary<SQuiLVariable, TableColumn> PkColumnOf { get; } = new();
    }

    /// <summary>Only OUTPUT (@Return_/@Returns_) table/object variables participate.</summary>
    internal static List<SQuiLVariable> OutputTableVariables(SQuiLParseResult parsed) =>
        parsed.Variables.Where(v =>
            (v.Role == VariableRole.Returns || v.Role == VariableRole.ReturnTable)
            && v.Columns is { Count: > 0 })
            .ToList();

    /// <summary>Only INPUT (@Param_/@Params_) table/object variables participate.</summary>
    internal static List<SQuiLVariable> InputTableVariables(SQuiLParseResult parsed) =>
        parsed.Variables.Where(v =>
            (v.Role == VariableRole.Params || v.Role == VariableRole.ParamTable)
            && v.Columns is { Count: > 0 })
            .ToList();

    internal static KeyGraph BuildKeyGraph(List<SQuiLVariable> list)
    {
        var graph = new KeyGraph();

        // Key column name -> owning variable(s). A variable's key = its single
        // Primary-Key column.
        var pkOwners = new Dictionary<string, List<SQuiLVariable>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var v in list)
        {
            var pk = v.Columns!.FirstOrDefault(c => c.IsPrimaryKey);
            if (pk is null) continue;
            graph.PkColumnOf[v] = pk;
            if (!pkOwners.TryGetValue(pk.Name, out var owners))
                pkOwners[pk.Name] = owners = new List<SQuiLVariable>();
            owners.Add(v);
        }

        foreach (var child in list)
        {
            // Which declared keys does this variable carry a matching column for
            // (excluding its own PK)?
            var matches = new List<(string Key, SQuiLVariable Parent)>();
            foreach (var col in child.Columns!)
            {
                if (!pkOwners.TryGetValue(col.Name, out var owners)) continue;
                foreach (var owner in owners)
                {
                    if (ReferenceEquals(owner, child)) continue; // own PK column
                    matches.Add((col.Name, owner));
                }
            }
            if (matches.Count == 0) continue;

            // A child column matching >1 distinct parent → ambiguous (graph must be a tree).
            var distinctParents = matches.Select(m => m.Parent).Distinct().ToList();
            if (distinctParents.Count > 1)
            {
                var other = distinctParents.First(p => !ReferenceEquals(p, distinctParents[0]));
                graph.Ambiguities.Add(new KeyGraphAmbiguity { Child = child, OtherParent = other });
                continue;
            }

            graph.Edges.Add(new KeyGraphEdge { Parent = distinctParents[0], Child = child, KeyName = matches[0].Key });
        }

        return graph;
    }

    /// <summary>
    /// Task 16 — relationship-key classification span list. Every column NAME
    /// token (line, character, length) that plays a role in the nested-object
    /// PK/FK-by-convention graph: a parent's designated Primary Key column,
    /// and every child column that resolves to it. Classification-only (never
    /// a diagnostic) — consumed by <c>SQuiLLinkedKeyClassifier</c>. Covers
    /// BOTH the OUTPUT and INPUT universes independently, never mixed, same
    /// as every other nested-object feature. Graceful degradation: a file
    /// with no links produces an empty list. Mirrors <c>linkedColumnRanges</c>
    /// in <c>linkedColumnRanges.ts</c> (VS Code) — change one side, change
    /// both (the exact span REPRESENTATION differs — LSP-style semantic
    /// tokens there vs. plain (line, character, length) tuples here, since
    /// this feeds a classic <c>IClassifier</c>, not a semantic-tokens API).
    /// </summary>
    internal static List<(int Line, int Character, int Length)> LinkedColumnSpans(SQuiLParseResult parsed)
    {
        var spans = new List<(int Line, int Character, int Length)>();
        var seen = new HashSet<(int, int, int)>();

        foreach (var list in new[] { OutputTableVariables(parsed), InputTableVariables(parsed) })
        {
            var graph = BuildKeyGraph(list);
            if (graph.Edges.Count == 0) continue;

            foreach (var edge in graph.Edges)
            {
                var pkCol = edge.Parent.Columns!.FirstOrDefault(c =>
                    c.IsPrimaryKey && string.Equals(c.Name, edge.KeyName, System.StringComparison.OrdinalIgnoreCase));
                if (pkCol is not null)
                {
                    var span = (pkCol.Line, pkCol.Character, pkCol.Name.Length);
                    if (seen.Add(span)) spans.Add(span);
                }

                var fkCol = edge.Child.Columns!.FirstOrDefault(c =>
                    string.Equals(c.Name, edge.KeyName, System.StringComparison.OrdinalIgnoreCase));
                if (fkCol is not null)
                {
                    var span = (fkCol.Line, fkCol.Character, fkCol.Name.Length);
                    if (seen.Add(span)) spans.Add(span);
                }
            }
        }

        return spans;
    }

    /// <summary>
    /// Nested-object link role text for the column at the given source
    /// position, or null when the position isn't on a column that plays a
    /// PK/FK-by-convention role (graceful degradation — hover is left
    /// unchanged). Searches OUTPUT variables first, then INPUT — a position
    /// can only ever land on one variable's column, so the search order isn't
    /// observable. Resolves the role against whichever universe the hit
    /// variable belongs to, never mixing OUTPUT and INPUT into one graph.
    /// Ported to hoverProvider.ts's <c>describeColumnLinkRole</c>
    /// (via linkRoleHints.ts) — change one side, change all three.
    /// </summary>
    internal static string? DescribeColumnLinkRole(SQuiLParseResult parsed, int line, int character)
    {
        var outputList = OutputTableVariables(parsed);
        var inputList = InputTableVariables(parsed);

        SQuiLVariable? owner = null;
        TableColumn? column = null;
        List<SQuiLVariable>? list = null;
        foreach (var candidates in new[] { outputList, inputList })
        {
            foreach (var v in candidates)
            {
                var hit = v.Columns!.FirstOrDefault(c =>
                    c.Line == line && character >= c.Character && character <= c.Character + c.Name.Length);
                if (hit is null) continue;
                owner = v;
                column = hit;
                list = candidates;
                break;
            }
            if (owner is not null) break;
        }
        if (owner is null || column is null || list is null) return null;

        var graph = BuildKeyGraph(list);

        if (column.IsPrimaryKey
            && graph.PkColumnOf.TryGetValue(owner, out var ownPk)
            && ReferenceEquals(ownPk, column))
        {
            bool hasChild = graph.Edges.Any(e => ReferenceEquals(e.Parent, owner));
            if (hasChild)
                return $"Primary Key — child tables that carry a `{column.Name}` column nest under `{owner.Name}`.";

            // Graceful degradation: in a file with no links at all, an "orphan" PK
            // note would fire on every table's PK, which is noise, not a hint. Only
            // surface the orphan note when at least one real link exists elsewhere
            // in the file (mirrors SP0035's `graph.Edges.Count > 0` gate).
            if (graph.Edges.Count == 0) return null;
            return $"Primary Key — no child table links to `{column.Name}` yet; add a matching column on a " +
                  $"child table to nest rows under `{owner.Name}`.";
        }

        var edge = graph.Edges.FirstOrDefault(e =>
            ReferenceEquals(e.Child, owner) && string.Equals(e.KeyName, column.Name, System.StringComparison.OrdinalIgnoreCase));
        if (edge is not null)
            return $"Foreign key by convention → rows of `{owner.Name}` nest under `{edge.Parent.Name}` (matched by `{column.Name}`).";

        return null;
    }

    internal static void LintKeyGraph(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);
        var outputList = OutputTableVariables(parsed);
        var inputList = InputTableVariables(parsed);
        var outputGraph = BuildKeyGraph(outputList);
        var inputGraph = BuildKeyGraph(inputList);

        foreach (var (list, graph) in new[] { (outputList, outputGraph), (inputList, inputGraph) })
        {
            LintOneKeyGraph(list, graph, diagnostics);
        }

        LintUnsupportedInputKeyType(inputGraph, diagnostics);
    }

    /// <summary>SP0033/SP0034/SP0035 for ONE key graph (either OUTPUT or INPUT) — called
    /// once per universe by <see cref="LintKeyGraph"/> so the two graphs stay independent.</summary>
    private static void LintOneKeyGraph(List<SQuiLVariable> list, KeyGraph graph, List<SQuiLDiagnostic> diagnostics)
    {
        foreach (var ambiguity in graph.Ambiguities)
        {
            var child = ambiguity.Child;
            var other = ambiguity.OtherParent;
            diagnostics.Add(new SQuiLDiagnostic
            {
                Message = $"`{child.Name}` (line {child.Line + 1}) links to more than one table — it also matches " +
                          $"`{other.Name}`'s (line {other.Line + 1}) primary key. A nested-object child must have " +
                          "exactly one parent — rename one of the key columns so only one match remains.",
                Line = child.Line,
                StartChar = child.Character,
                EndChar = child.Character + child.RawName.Length,
                Severity = DiagnosticSeverity.Error,
                Code = "SP0033",
                RelatedLine = other.Line,
                RelatedStartChar = other.Character,
                RelatedEndChar = other.Character + other.RawName.Length,
                RelatedMessage = "matches this table's primary key",
            });
        }

        var childOf = graph.Edges.ToDictionary(e => e.Child, e => e.Parent);

        // Cycle / self-reference detection over the childOf map. Report each cycle
        // ONCE and name the actual partner (cur) whose FK closes the loop back to start.
        var reportedCycle = new HashSet<SQuiLVariable>();
        foreach (var start in list)
        {
            if (reportedCycle.Contains(start)) continue;
            var seen = new HashSet<SQuiLVariable>();
            var cur = start;
            while (childOf.TryGetValue(cur, out var next))
            {
                if (ReferenceEquals(next, start))
                {
                    diagnostics.Add(new SQuiLDiagnostic
                    {
                        Message = $"`{start.Name}` (line {start.Line + 1}) and `{cur.Name}` (line {cur.Line + 1}) " +
                                  "form a primary-key/foreign-key cycle. Nested objects cannot be recursive — remove one of the links.",
                        Line = start.Line,
                        StartChar = start.Character,
                        EndChar = start.Character + start.RawName.Length,
                        Severity = DiagnosticSeverity.Error,
                        Code = "SP0034",
                        RelatedLine = cur.Line,
                        RelatedStartChar = cur.Character,
                        RelatedEndChar = cur.Character + cur.RawName.Length,
                        RelatedMessage = "cycle partner declared here",
                    });
                    // Mark every member of this cycle so it is not re-reported from another start.
                    reportedCycle.Add(start);
                    var w = start;
                    while (childOf.TryGetValue(w, out var n) && reportedCycle.Add(n))
                        w = n;
                    break;
                }
                if (!seen.Add(next)) break;
                cur = next;
            }
        }

        // SP0035: orphan PK hint — only when at least one real link exists (hasLinks).
        if (graph.Edges.Count > 0)
        {
            foreach (var kv in graph.PkColumnOf)
            {
                var v = kv.Key;
                var col = kv.Value;
                if (graph.Edges.Any(e => ReferenceEquals(e.Parent, v))) continue;

                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message = $"Primary Key `{col.Name}` on `{v.Name}` has no child linking to it — no nesting will occur; " +
                              "add a matching column on a child table, or remove the key.",
                    Line = col.Line,
                    StartChar = col.Character,
                    EndChar = col.Character + col.Name.Length,
                    Severity = DiagnosticSeverity.Info,
                    Code = "SP0035",
                });
            }
        }
    }

    /// <summary>SQL types the generator can synthesize a nested-input join key for
    /// (<c>IsSynthesizableKeyType</c> in FileGenerator.cs): integer-family + uniqueidentifier.</summary>
    private static bool IsSynthesizableKeyType(string sqlType)
    {
        var t = sqlType.Trim();
        int paren = t.IndexOf('(');
        if (paren >= 0) t = t.Substring(0, paren);
        var word = t.Split(' ')[0];
        return string.Equals(word, "int", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(word, "bigint", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(word, "smallint", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(word, "uniqueidentifier", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// SP0036 (Error) — within the nested-INPUT key graph, a parent/child link
    /// column's declared type is neither integer-family (int/bigint/smallint) nor
    /// uniqueidentifier, so the generator cannot synthesize a join key for it.
    /// Mirrors the build error (IsSynthesizableKeyType / ReportUnsupportedKeyType
    /// in FileGenerator.cs/DiagnosticsMessages.cs) — editor-squiggle parity,
    /// checked only against the INPUT graph (OUTPUT graphs never synthesize keys).
    /// </summary>
    private static void LintUnsupportedInputKeyType(KeyGraph inputGraph, List<SQuiLDiagnostic> diagnostics)
    {
        foreach (var edge in inputGraph.Edges)
        {
            var keyColumn = edge.Parent.Columns?.FirstOrDefault(c =>
                c.IsPrimaryKey && string.Equals(c.Name, edge.KeyName, System.StringComparison.OrdinalIgnoreCase))
                ?? edge.Parent.Columns?.FirstOrDefault(c =>
                    string.Equals(c.Name, edge.KeyName, System.StringComparison.OrdinalIgnoreCase));
            if (keyColumn is null) continue;
            if (IsSynthesizableKeyType(keyColumn.SqlType)) continue;

            var child = edge.Child;
            diagnostics.Add(new SQuiLDiagnostic
            {
                Message = $"Link column `{edge.KeyName}` on `{child.Name}` (line {child.Line + 1}) has type " +
                          $"`{keyColumn.SqlType}`, which cannot have a join key synthesized. A nested-input key column " +
                          "must be an integer type (int, bigint, or smallint) or uniqueidentifier — change the link column's type.",
                Line = child.Line,
                StartChar = child.Character,
                EndChar = child.Character + child.RawName.Length,
                Severity = DiagnosticSeverity.Error,
                Code = "SP0036",
            });
        }
    }

    // ── Orphan / duplicate context resolver (SP0028 / SP0027) ────────────────
    //
    // SP0028 (Warning): this .squil file isn't registered by any data context.
    // SP0027 (Error):   multiple data contexts register the same .squil file.
    //
    // Port of the SP0028/SP0027 block in diagnosticsProvider.ts (VS Code) —
    // change one side, change all three.

    internal static void LintOrphanContext(string squilFilePath, List<SQuiLDiagnostic> diagnostics)
    {
        var ctx = SQuiLContextResolver.Resolve(squilFilePath);
        if (ctx.Found) return;

        if (ctx.MatchCount == 0)
        {
            diagnostics.Add(new SQuiLDiagnostic
            {
                Message   = "This query file isn't registered by any data context. " +
                            "Add a [SQuiLQuery] or [SQuiLQueryTransaction] attribute referencing it.",
                Line      = 0,
                StartChar = 0,
                EndChar   = 0,
                Severity  = DiagnosticSeverity.Warning,
                Code      = "SP0028",
            });
        }
        else
        {
            diagnostics.Add(new SQuiLDiagnostic
            {
                Message   = $"This query file is registered by {ctx.MatchCount} data contexts. " +
                            "Only one [SQuiLQuery] or [SQuiLQueryTransaction] may reference each QueryFiles member.",
                Line      = 0,
                StartChar = 0,
                EndChar   = 0,
                Severity  = DiagnosticSeverity.Error,
                Code      = "SP0027",
            });
        }
    }

    // ── Mutation-vs-transaction diagnostics (SP0023 / SP0024 / SP0025) ──────
    //
    // SP0023 (Warning): [SQuiLQuery] or disabled transaction wraps a body with a
    //   persistent real-table mutation (UPDATE/INSERT/DELETE/MERGE/EXEC/…).
    // SP0024 (Warning): [SQuiLQueryTransaction] enabled wraps a provably read-only body.
    // SP0025 (Error):   [SQuiLQueryTransaction] enabled body contains its own Begin Tran.
    //
    // Port of the build-time emit in FileGenerator.cs and the SP0023/SP0024/SP0025
    // block in diagnosticsProvider.ts (VS Code) — change one, change all three.

    internal static void LintMutationDiagnostics(string sql, string squilFilePath, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var ctx = SQuiLContextResolver.Resolve(squilFilePath);
        if (!ctx.Found) return; // orphan/duplicate already reported by LintOrphanContext

        // Extract the body text (dialect-aware). For T-SQL the body starts on the line AFTER the
        // USE statement (DatabaseLine + 1). Temp-table-header dialects (SQLite, PostgreSQL) have
        // NO USE — their header is Create-Temp-Table declarations — so DatabaseLine is null there;
        // the body begins after the leading declarations (and any param-table population), as
        // computed by SqliteBodyStartLine. Without this, the body would be empty, making the SP0025
        // Begin regex dead and drawing a spurious SP0024 on real mutations.
        var parsed = SQuiLParser.Parse(sql, dialect);

        int bodyStartLine;
        if (SQuiLDialect.IsTempTableDialect(dialect))
        {
            bodyStartLine = SQuiLParser.SqliteBodyStartLine(sql, parsed);
        }
        else
        {
            if (parsed.DatabaseLine is not { } databaseLine) return;
            bodyStartLine = databaseLine + 1;
        }

        var lines = sql.Split('\n');
        // Compute the character offset of the first body line.
        int bodyStartOffset = 0;
        for (int i = 0; i < bodyStartLine && i < lines.Length; i++)
            bodyStartOffset += lines[i].Length + 1; // +1 for the '\n'

        var bodyText = bodyStartOffset < sql.Length ? sql.Substring(bodyStartOffset) : string.Empty;

        var scan = SQuiLMutationScanner.Scan(bodyText, dialect);

        if (!ctx.Enabled)
        {
            // [SQuiLQuery] or [SQuiLQueryTransaction(enabled:false)] — warn if mutation detected.
            if (!scan.IsProvablyReadOnly && scan.Mutations.Count > 0)
            {
                var hit = scan.Mutations[0];
                var hitAbsOffset = bodyStartOffset + hit.Start;
                var (hitLine, hitChar) = OffsetToLineChar(sql, hitAbsOffset);
                var hitEndChar = hitChar + hit.Length;

                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message   = $"The query body contains a persistent real-table mutation ({hit.Kind}). " +
                                "Use [SQuiLQueryTransaction] to wrap the mutation in a transaction.",
                    Line      = hitLine,
                    StartChar = hitChar,
                    EndChar   = hitEndChar,
                    Severity  = DiagnosticSeverity.Warning,
                    Code      = "SP0023",
                });
            }
        }
        else
        {
            // [SQuiLQueryTransaction(enabled:true)] — warn if read-only; error if own Begin Tran.
            if (scan.IsProvablyReadOnly)
            {
                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message   = "No persistent mutation was detected in the query body. " +
                                "Use [SQuiLQuery] instead — a transaction wrapper adds overhead with no benefit on a read-only query.",
                    Line      = 0,
                    StartChar = 0,
                    EndChar   = 0,
                    Severity  = DiagnosticSeverity.Warning,
                    Code      = "SP0024",
                });
            }

            if (scan.HasOwnTransaction)
            {
                // Try to locate the Begin Tran in the body for a precise range. The temp-table
                // dialect family (SQLite, PostgreSQL) also starts a transaction with a bare `BEGIN`
                // (or BEGIN TRANSACTION), so widen the range regex there.
                var btMatch = System.Text.RegularExpressions.Regex.Match(
                    bodyText,
                    SQuiLDialect.IsTempTableDialect(dialect) ? @"\bBegin(?:\s+Transaction)?\b" : @"\bBegin\s+Tran(?:saction)?\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                int btLine = 0, btChar = 0, btEndChar = 0;
                if (btMatch.Success)
                {
                    var btAbsOffset = bodyStartOffset + btMatch.Index;
                    (btLine, btChar) = OffsetToLineChar(sql, btAbsOffset);
                    btEndChar = btChar + btMatch.Length;
                }

                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message   = "The query body contains a Begin Tran/Begin Transaction statement, but " +
                                "[SQuiLQueryTransaction] already wraps the query in a C# DbTransaction. " +
                                "Remove the SQL-level transaction, or set enabled:false on [SQuiLQueryTransaction] to manage the transaction manually.",
                    Line      = btLine,
                    StartChar = btChar,
                    EndChar   = btEndChar,
                    Severity  = DiagnosticSeverity.Error,
                    Code      = "SP0025",
                });
            }
        }
    }

    // ── debugRollback-without-Debug hint (SP0026) ───────────────────────────
    //
    // SP0026 (Info): [SQuiLQueryTransaction] has debugRollback:true (the default)
    // but the file does NOT declare @Debug.  Without @Debug the debug-rollback
    // branch is unreachable — the setting is inert.
    //
    // Trigger: context found + attribute SQuiLQueryTransaction + debugRollback=true
    //          + no @Debug declared in the SQL text.
    // Severity: Info (C# extensions have no Hint enum value — mirrors SP0010/SP0020).
    //
    // Port of transactionHints.ts (VS Code extension, Hint severity there) —
    // change one side, change all three.

    internal static void LintDebugRollbackHint(string sql, string squilFilePath, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var ctx = SQuiLContextResolver.Resolve(squilFilePath);
        if (!ctx.Found) return;
        if (ctx.Attribute != "SQuiLQueryTransaction") return;
        if (!ctx.Enabled) return;
        if (!ctx.DebugRollback) return;

        // Check whether @Debug is declared anywhere in the file.
        var parsed = SQuiLParser.Parse(sql, dialect);
        bool hasDebug = parsed.Variables.Any(v => v.Role == VariableRole.Debug);
        if (hasDebug) return;

        diagnostics.Add(new SQuiLDiagnostic
        {
            Message   = "`debugRollback: true` has no effect without a declared `@Debug`. " +
                        "Declare `@Debug bit;` in the header, or set `debugRollback: false` on [SQuiLQueryTransaction].",
            Line      = 0,
            StartChar = 0,
            EndChar   = 0,
            Severity  = DiagnosticSeverity.Info,
            Code      = "SP0026",
        });
    }

    /// <summary><c>internal</c> (not <c>private</c>) so <c>SQuiLSuggestedActionsSource</c>
    /// can convert a <see cref="BareScalarSelect"/>'s offset to a (line, character) pair
    /// when building the SP0042 quick-fix, without duplicating this conversion.</summary>
    internal static (int Line, int Char) OffsetToLineChar(string text, int offset)
    {
        int line = 0, charPos = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') { line++; charPos = 0; }
            else charPos++;
        }
        return (line, charPos);
    }

    // ── Shape-mismatch detection (SP0017) ────────────────────────────────────
    //
    // Within a single file, detect table variables that share the same base name
    // (after stripping @Returns_/@Return_/@Params_/@Param_ prefixes) but declare
    // different column shapes.  Fires the second declaration as the primary
    // location and carries a related-information pointer back to the first.
    //
    // Port of lintShapeMismatch() in parser.ts (VS Code extension) — change one, change all.

    internal static void LintShapeMismatch(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);
        var tableVars = parsed.Variables.Where(v =>
            (v.Role == VariableRole.Returns   || v.Role == VariableRole.ReturnTable ||
             v.Role == VariableRole.Params    || v.Role == VariableRole.ParamTable)
            && v.Columns != null && v.Columns.Count > 0)
            .ToList();

        // Size-independent: strip the (...) size suffix from each SQL type before
        // comparing — mirrors the generator's SameShape (sizes may differ).
        static string StripSize(string t) => Regex.Replace(t, @"\s*\([^)]*\)", "").ToLowerInvariant();

        var seen = new Dictionary<string, SQuiLVariable>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var v in tableVars)
        {
            string sig = string.Join("|", v.Columns!.Select(c => $"{c.Name}:{StripSize(c.SqlType)}:{c.Nullable}"));
            if (!seen.TryGetValue(v.Name, out var first))
            {
                seen[v.Name] = v;
                continue;
            }
            string firstSig = string.Join("|", first.Columns!.Select(c => $"{c.Name}:{StripSize(c.SqlType)}:{c.Nullable}"));
            if (sig == firstSig) continue;

            diagnostics.Add(new SQuiLDiagnostic
            {
                Message       = $"All declarations that generate the record `{v.Name}` must declare identical columns " +
                                $"(same names, types, nullability, and order). " +
                                $"Rename one of the variables or align the column lists.",
                Line          = v.Line,
                StartChar     = v.Character,
                EndChar       = v.Character + v.RawName.Length,
                Severity      = DiagnosticSeverity.Error,
                Code          = "SP0017",
                RelatedLine   = first.Line,
                RelatedStartChar = first.Character,
                RelatedEndChar   = first.Character + first.RawName.Length,
                RelatedMessage   = "first declared here",
            });
        }
    }

    // ── Cardinality-collision detection (SP0022) ─────────────────────────────
    //
    // Within a single file, a base name declared as BOTH a table (list:
    // @Params_/@Returns_) AND a single object (@Param_…table/@Return_…table) on the
    // SAME side (both inputs → request, or both outputs → response) resolves to one
    // request/response property; the generator keeps the first and silently drops the
    // rest.  Warns on the first declaration and errors on each subsequent one.
    //
    // Port of lintCardinalityCollision() in parser.ts (VS Code) and
    // SQuiLCardinalityValidator.cs (source generator) — change one, change all.

    internal static void LintCardinalityCollision(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);

        static bool IsList(VariableRole r) => r == VariableRole.Params || r == VariableRole.Returns;
        static bool IsObject(VariableRole r) => r == VariableRole.ParamTable || r == VariableRole.ReturnTable;
        static string Kind(VariableRole r) => IsList(r) ? "a table" : "a single object";

        var tableVars = parsed.Variables.Where(v => IsList(v.Role) || IsObject(v.Role)).ToList();

        var groups = new Dictionary<string, List<SQuiLVariable>>();
        foreach (var v in tableVars)
        {
            bool isOutput = v.Role == VariableRole.Returns || v.Role == VariableRole.ReturnTable;
            string key = (isOutput ? "out:" : "in:") + v.Name.ToLowerInvariant();
            if (!groups.TryGetValue(key, out var g))
            {
                g = new List<SQuiLVariable>();
                groups[key] = g;
            }
            g.Add(v);
        }

        foreach (var group in groups.Values)
        {
            if (!group.Any(v => IsList(v.Role)) || !group.Any(v => IsObject(v.Role))) continue;

            var first = group[0];

            // Only declarations whose cardinality DIFFERS from the winner are conflicts.
            // A same-cardinality duplicate (e.g. a second @Returns_X) is a plain dedup,
            // not a collision — exclude it so 3+ same-name groups flag only the mismatches.
            var conflicts = group.Skip(1).Where(v => IsList(v.Role) != IsList(first.Role)).ToList();
            if (conflicts.Count == 0) continue;

            diagnostics.Add(new SQuiLDiagnostic
            {
                Message = $"`{first.RawName}` declares `{first.Name}` as {Kind(first.Role)}, but `{conflicts[0].RawName}` (line {conflicts[0].Line + 1}) declares it as {Kind(conflicts[0].Role)}. " +
                          "One cardinality wins and the other is silently dropped — rename one variable, or use the same cardinality for both.",
                Line = first.Line,
                StartChar = first.Character,
                EndChar = first.Character + first.RawName.Length,
                Severity = DiagnosticSeverity.Warning,
                Code = "SP0022",
                RelatedLine = conflicts[0].Line,
                RelatedStartChar = conflicts[0].Character,
                RelatedEndChar = conflicts[0].Character + conflicts[0].RawName.Length,
                RelatedMessage = "conflicting cardinality declared here",
            });

            foreach (var v in conflicts)
            {
                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message = $"`{v.RawName}` declares `{v.Name}` as {Kind(v.Role)}, but `{first.RawName}` already declares it as {Kind(first.Role)} (line {first.Line + 1}). " +
                              "One cardinality wins and the other is silently dropped — rename one variable, or use the same cardinality for both.",
                    Line = v.Line,
                    StartChar = v.Character,
                    EndChar = v.Character + v.RawName.Length,
                    Severity = DiagnosticSeverity.Error,
                    Code = "SP0022",
                    RelatedLine = first.Line,
                    RelatedStartChar = first.Character,
                    RelatedEndChar = first.Character + first.RawName.Length,
                    RelatedMessage = "first declared here",
                });
            }
        }
    }

    // ── Result-shape collision detection (SP0030) ────────────────────────────
    //
    // Within a single file, detect OUTPUT table variables (Returns / ReturnTable)
    // that have DISTINCT names but IDENTICAL canonical shape keys (same column
    // names, order, and C# types — length/precision does NOT differentiate).
    // When two or more outputs share a key the runtime cannot route result sets
    // to different records; all are flagged as errors with cross-referencing
    // related information.
    //
    // Same-name is NOT a collision (same-name + different shape = SP0017's domain).
    //
    // Port of lintShapeCollision() in parser.ts (VS Code extension) —
    // change one, change all three.

    internal static void LintShapeCollision(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);

        static string CanonicalType(string sqlType)
        {
            string cs = SqlTypeMap.SqlToCSharp(sqlType);
            return cs; // SqlToCSharp already strips size/precision; no '?' suffix to strip
        }

        static string ShapeKeyOf(List<TableColumn> columns) =>
            string.Join("|", columns.Select(c => $"{c.Name.ToLowerInvariant()}:{CanonicalType(c.SqlType)}"));

        var outputs = parsed.Variables.Where(v =>
            (v.Role == VariableRole.Returns || v.Role == VariableRole.ReturnTable)
            && v.Columns != null && v.Columns.Count > 0)
            .ToList();

        // Group by canonical shape key.
        var byKey = new Dictionary<string, List<SQuiLVariable>>();
        foreach (var v in outputs)
        {
            string key = ShapeKeyOf(v.Columns!);
            if (!byKey.TryGetValue(key, out var group))
            {
                group = new List<SQuiLVariable>();
                byKey[key] = group;
            }
            group.Add(v);
        }

        foreach (var group in byKey.Values)
        {
            // Deduplicate by name (OrdinalIgnoreCase) — only distinct names are a collision.
            var distinct = group
                .Where((v, i) => group.FindIndex(g =>
                    string.Equals(g.Name, v.Name, System.StringComparison.OrdinalIgnoreCase)) == i)
                .ToList();
            if (distinct.Count < 2) continue;

            var winner = distinct[0];
            for (int i = 0; i < distinct.Count; i++)
            {
                var self = distinct[i];
                var other = i == 0 ? distinct[1] : winner;
                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message        = $"`{self.RawName}` has the same result signature as `{other.RawName}` " +
                                     $"(line {other.Line + 1}) — identical column names, order, and C# types " +
                                     $"(length/precision does not differentiate). Result sets can't be routed apart. " +
                                     $"Differentiate a column, or share one name.",
                    Line           = self.Line,
                    StartChar      = self.Character,
                    EndChar        = self.Character + self.RawName.Length,
                    Severity       = DiagnosticSeverity.Error,
                    Code           = "SP0030",
                    RelatedLine    = other.Line,
                    RelatedStartChar  = other.Character,
                    RelatedEndChar    = other.Character + other.RawName.Length,
                    RelatedMessage    = "conflicting result signature declared here",
                });
            }
        }
    }

    // ── Similar-signature hints (SP0020) ────────────────────────────────────
    //
    // Emits an Info-level hint for every table/object variable that shares an
    // EXACT column signature (same names, types, nullability, and order) with
    // a DIFFERENTLY-named variable in the same file.  This is the complement
    // of LintShapeMismatch (SP0017), which fires on same-name + different shape.
    //
    // Trigger:  different name + same signature.
    // Silent:   same name (SP0017's domain), or no matching signature found.
    // SP0030 reconciliation: same-file same-side OUTPUT pairs with identical
    // canonical shape are now SP0030's domain — exclude them from SP0020.
    //
    // Port of shapeHints.ts (VS Code extension) — change one, change all three.

    internal static void LintSimilarSignatures(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);
        var tableVars = parsed.Variables.Where(v =>
            (v.Role == VariableRole.Returns   || v.Role == VariableRole.ReturnTable ||
             v.Role == VariableRole.Params    || v.Role == VariableRole.ParamTable)
            && v.Columns != null && v.Columns.Count > 0)
            .ToList();

        if (tableVars.Count < 2) return;

        // SP0030 reconciliation: compute the set of output RawNames already flagged
        // by LintShapeCollision (same-side output pairs with identical canonical key).
        // Those must NOT also get an SP0020 hint.
        static string CanonicalKey(List<TableColumn> cols) =>
            string.Join("|", cols.Select(c => $"{c.Name.ToLowerInvariant()}:{SqlTypeMap.SqlToCSharp(c.SqlType)}"));

        var sp0030Names = new HashSet<string>(System.StringComparer.Ordinal);
        {
            var outputVars = tableVars.Where(v =>
                v.Role == VariableRole.Returns || v.Role == VariableRole.ReturnTable).ToList();
            var outputByKey = new Dictionary<string, List<SQuiLVariable>>();
            foreach (var v in outputVars)
            {
                string key = CanonicalKey(v.Columns!);
                if (!outputByKey.TryGetValue(key, out var g)) { g = new(); outputByKey[key] = g; }
                g.Add(v);
            }
            foreach (var g in outputByKey.Values)
            {
                var distinct = g.Where((v, i) =>
                    g.FindIndex(x => string.Equals(x.Name, v.Name, System.StringComparison.OrdinalIgnoreCase)) == i)
                    .ToList();
                if (distinct.Count >= 2)
                    foreach (var v in distinct) sp0030Names.Add(v.RawName);
            }
        }

        // Build signature → list of variables.
        // Size-independent: strip the (...) size suffix — mirrors the generator's SameShape.
        static string StripSize(string t) => Regex.Replace(t, @"\s*\([^)]*\)", "").ToLowerInvariant();
        var bySig = new Dictionary<string, List<SQuiLVariable>>();
        foreach (var v in tableVars)
        {
            string sig = string.Join("|", v.Columns!.Select(c =>
                $"{c.Name}:{StripSize(c.SqlType)}:{(c.Nullable ? "N" : "NN")}"));
            if (!bySig.TryGetValue(sig, out var group))
            {
                group = new List<SQuiLVariable>();
                bySig[sig] = group;
            }
            group.Add(v);
        }

        foreach (var group in bySig.Values)
        {
            if (group.Count < 2) continue;

            // Distinct base names — same-name groups belong to SP0017.
            var distinctNames = new HashSet<string>(
                group.Select(v => v.Name), System.StringComparer.OrdinalIgnoreCase);
            if (distinctNames.Count < 2) continue;

            foreach (var a in group)
            {
                // SP0030 reconciliation: skip variables already covered by SP0030.
                if (sp0030Names.Contains(a.RawName)) continue;

                // Find the first differently-named partner to mention.
                var partner = group.FirstOrDefault(b =>
                    !string.Equals(b.Name, a.Name, System.StringComparison.OrdinalIgnoreCase));
                if (partner == null) continue;

                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message   = $"`{a.Name}` has the same column signature as `{partner.Name}` " +
                                $"(line {partner.Line + 1}). " +
                                $"If these are the same shape, give them the same name to share one generated type.",
                    Line      = a.Line,
                    StartChar = a.Character,
                    EndChar   = a.Character + a.RawName.Length,
                    Severity  = DiagnosticSeverity.Info,
                    Code      = "SP0020",
                });
            }
        }
    }

    // ── Nullability hints (SP0010) ───────────────────────────────────────────
    //
    // Emits an Info-level hint for every scalar @Param_* / @Return_* variable
    // and every table column that carries no explicit NULL / NOT NULL marker.
    // When left unmarked the generator produces a non-nullable C# type; the hint
    // nudges the author to make the intent explicit.
    //
    // Port of nullabilityHints.ts (VS Code extension) — message must stay
    // byte-exact across all three editor surfaces.

    internal static void LintNullabilityHints(string sql, List<SQuiLDiagnostic> diagnostics, EditorDialect dialect = EditorDialect.SqlServer)
    {
        var parsed = SQuiLParser.Parse(sql, dialect);
        foreach (var v in parsed.Variables)
        {
            if (v.Columns is { Count: > 0 })
            {
                // Table variable — check each column individually.
                foreach (var col in v.Columns)
                {
                    if (col.NullabilityMarker is null)
                    {
                        string csType = SqlTypeMap.SqlToCSharp(col.SqlType);
                        diagnostics.Add(new SQuiLDiagnostic
                        {
                            Message   = $"No `null`/`not null` marker — generated C# is non-nullable `{csType} {col.Name}`. "
                                      + $"Add `not null` to confirm, or `null` to make it nullable.",
                            Line      = v.Line,
                            StartChar = v.Character,
                            EndChar   = v.Character + col.Name.Length,
                            Severity  = DiagnosticSeverity.Info,
                        });
                    }
                }
            }
            else if ((v.Role == VariableRole.Param || v.Role == VariableRole.Return)
                     && !v.Nullable && v.NullabilityMarker is null)
            {
                string csType = SqlTypeMap.SqlToCSharp(v.SqlType);
                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message   = $"No `= null` — generated C# is non-nullable `{csType} {v.Name}`. "
                              + $"Add `= null` to make it nullable.",
                    Line      = v.Line,
                    StartChar = v.Character,
                    EndChar   = v.Character + v.Name.Length,
                    Severity  = DiagnosticSeverity.Info,
                });
            }
        }
    }

    private static void LintCasing(string line, int lineNum, List<SQuiLDiagnostic> diagnostics)
    {
        foreach (var (pattern, correct) in TypoPatterns)
        {
            var m = pattern.Match(line);
            if (!m.Success) continue;

            string actual = m.Value;
            // Only flag when the casing differs from canonical PascalCase.
            // (The TS implementation has a clearer guard for this; the second
            // clause there is redundant — equivalent to "actual != correct".)
            if (actual == correct) continue;

            diagnostics.Add(new SQuiLDiagnostic
            {
                Message  = $"Prefer '{correct}' (PascalCase) over '{actual}'. "
                         + "SQuiL uses PascalCase for variable prefixes.",
                Line     = lineNum,
                StartChar= m.Index,
                EndChar  = m.Index + actual.Length,
                Severity = DiagnosticSeverity.Info,
            });
        }
    }

    private static void LintMissingSemicolon(string line, int lineNum, List<SQuiLDiagnostic> diagnostics)
    {
        string trimmed = line.TrimEnd();
        if (trimmed.EndsWith(";")) return;
        if (BlockCommentEnd.IsMatch(trimmed)) return;

        // Multi-line TABLE declarations defer the semicolon to the closing
        // line — skip while the open paren has not been balanced yet.
        if (TableOpenWithoutClose.IsMatch(trimmed) && !trimmed.Contains(")"))
            return;

        diagnostics.Add(new SQuiLDiagnostic
        {
            Message  = "DECLARE statement is missing a semicolon terminator.",
            Line     = lineNum,
            StartChar= trimmed.Length,
            EndChar  = trimmed.Length,
            Severity = DiagnosticSeverity.Info,
        });
    }

    // ── Undeclared-variable / special-placement validation ──────────────────
    //
    // A SQuiL file must be valid T-SQL: every @variable reference needs a
    // textually-preceding DECLARE for that exact name (SQL Server rejects the
    // whole batch otherwise) — no remapping, no implicit specials. @Debug and
    // @EnvironmentName must additionally be declared before the USE statement,
    // and preferably before any other declaration.
    //
    // Port of SQuiLVariableValidator.cs (source generator) and
    // variableValidator.ts (VS Code extension) — change one, change the others.

    private enum ScanState { Normal, ExpectVariable, InType, InDefault }

    private static readonly HashSet<string> StatementStarters = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "SET", "IF", "WHILE", "BEGIN", "END",
        "USE", "DECLARE", "EXEC", "EXECUTE", "WITH", "MERGE", "PRINT", "RETURN",
        "CREATE", "DROP", "ALTER", "TRUNCATE", "GO",
    };

    private static bool IsSpecialVariable(string name)
        => string.Equals(name, "@Debug", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "@SuppressDebug", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "@EnvironmentName", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "@AsOfDate", System.StringComparison.OrdinalIgnoreCase);

    private static bool IsNameChar(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '#';

    internal static void LintUndeclaredVariables(string sql, List<SQuiLDiagnostic> diagnostics)
    {
        string text = MaskNonCode(sql);

        var declarations = new List<KeyValuePair<string, int>>(); // name → offset
        var references = new List<KeyValuePair<string, int>>();
        int? useOffset = null;

        var state = ScanState.Normal;
        int parenDepth = 0;
        int caseDepth = 0;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '(') { parenDepth++; i++; continue; }
            if (c == ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

            if (c == ';')
            {
                if (parenDepth == 0) { state = ScanState.Normal; caseDepth = 0; }
                i++;
                continue;
            }

            if (c == ',')
            {
                if (parenDepth == 0 && (state == ScanState.InType || state == ScanState.InDefault))
                    state = ScanState.ExpectVariable;
                i++;
                continue;
            }

            if (c == '=')
            {
                if (parenDepth == 0 && state == ScanState.InType)
                    state = ScanState.InDefault;
                i++;
                continue;
            }

            if (c == '@')
            {
                int start = i;
                i++;
                if (i < text.Length && text[i] == '@')
                {
                    // system variable (@@ROWCOUNT etc.) — skip the whole token
                    i++;
                    while (i < text.Length && IsNameChar(text[i])) i++;
                    continue;
                }

                int nameStart = i;
                while (i < text.Length && IsNameChar(text[i])) i++;
                if (i == nameStart) continue; // a lone '@' is not a variable

                string name = text.Substring(start, i - start);

                if (state == ScanState.ExpectVariable)
                {
                    declarations.Add(new KeyValuePair<string, int>(name, start));
                    state = ScanState.InType;
                }
                else
                {
                    references.Add(new KeyValuePair<string, int>(name, start));
                }
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < text.Length && IsNameChar(text[i])) i++;
                string word = text.Substring(start, i - start);

                if (word.Equals("DECLARE", System.StringComparison.OrdinalIgnoreCase))
                {
                    state = ScanState.ExpectVariable;
                    continue;
                }

                if (state == ScanState.Normal && useOffset == null && parenDepth == 0
                    && word.Equals("USE", System.StringComparison.OrdinalIgnoreCase))
                {
                    useOffset = start;
                    continue;
                }

                // CASE…END pairs inside a default-value expression must not end
                // the declare statement when END is reached.
                if (state == ScanState.InDefault && word.Equals("CASE", System.StringComparison.OrdinalIgnoreCase))
                {
                    caseDepth++;
                    continue;
                }
                if (state == ScanState.InDefault && caseDepth > 0 && word.Equals("END", System.StringComparison.OrdinalIgnoreCase))
                {
                    caseDepth--;
                    continue;
                }

                if (parenDepth == 0
                    && (state == ScanState.InType || state == ScanState.InDefault)
                    && StatementStarters.Contains(word))
                {
                    state = ScanState.Normal;
                    caseDepth = 0;
                    // no semicolon between the declare and the next statement —
                    // re-read the word in Normal state so DECLARE/USE chains work
                    i = start;
                }
                continue;
            }

            i++;
        }

        foreach (var reference in references)
        {
            bool declaredBefore = false;
            bool declaredAnywhere = false;
            foreach (var declaration in declarations)
            {
                if (!declaration.Key.Equals(reference.Key, System.StringComparison.OrdinalIgnoreCase)) continue;
                declaredAnywhere = true;
                if (declaration.Value < reference.Value) { declaredBefore = true; break; }
            }

            if (declaredBefore) continue;

            AddFinding(sql, diagnostics, reference.Key, reference.Value, DiagnosticSeverity.Error,
                declaredAnywhere
                    ? $"Variable '{reference.Key}' is referenced before its declaration. Move the Declare above the first use."
                    : $"Variable '{reference.Key}' is referenced but never declared. SQuiL files must be valid T-SQL — declare it before use.");
        }

        foreach (var declaration in declarations)
        {
            if (!IsSpecialVariable(declaration.Key)) continue;

            if (useOffset.HasValue && declaration.Value > useOffset.Value)
            {
                AddFinding(sql, diagnostics, declaration.Key, declaration.Value, DiagnosticSeverity.Error,
                    $"'{declaration.Key}' must be declared before the Use statement.");
                continue;
            }

            foreach (var other in declarations)
            {
                if (other.Value >= declaration.Value || IsSpecialVariable(other.Key)) continue;

                AddFinding(sql, diagnostics, declaration.Key, declaration.Value, DiagnosticSeverity.Warning,
                    $"'{declaration.Key}' should be declared at the top of the header, before other declarations.");
                break;
            }
        }

        // @SuppressDebug only has meaning alongside @Debug (it gates the
        // auto-debug expression). Declaring it without @Debug is an error —
        // mirrors the generator's SP0019 (SuppressDebugWithoutDebug finding).
        bool hasDebug = false;
        foreach (var declaration in declarations)
            if (string.Equals(declaration.Key, "@Debug", System.StringComparison.OrdinalIgnoreCase))
            {
                hasDebug = true;
                break;
            }

        if (!hasDebug)
            foreach (var declaration in declarations)
            {
                if (!string.Equals(declaration.Key, "@SuppressDebug", System.StringComparison.OrdinalIgnoreCase)) continue;
                AddFinding(sql, diagnostics, declaration.Key, declaration.Value, DiagnosticSeverity.Error,
                    $"'{declaration.Key}' may only be declared when '@Debug' is also declared in the same file.");
            }
    }

    private static void AddFinding(
        string sql, List<SQuiLDiagnostic> diagnostics,
        string name, int offset, DiagnosticSeverity severity, string message)
    {
        int line = 0, character = 0;
        for (int i = 0; i < offset && i < sql.Length; i++)
        {
            if (sql[i] == '\n') { line++; character = 0; }
            else character++;
        }

        diagnostics.Add(new SQuiLDiagnostic
        {
            Message = message,
            Line = line,
            StartChar = character,
            EndChar = character + name.Length,
            Severity = severity,
        });
    }

    /// <summary>
    /// Replaces comments (line and nested block), string literals, and bracketed
    /// identifiers with spaces so the scanner never sees their contents. Offsets
    /// and newlines are preserved.
    /// </summary>
    private static string MaskNonCode(string sql)
    {
        char[] chars = sql.ToCharArray();
        int i = 0;

        while (i < chars.Length)
        {
            char c = chars[i];

            if (c == '-' && i + 1 < chars.Length && chars[i + 1] == '-')
            {
                while (i < chars.Length && chars[i] != '\n') chars[i++] = ' ';
                continue;
            }

            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                int depth = 0;
                while (i < chars.Length)
                {
                    if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
                    {
                        depth++;
                        chars[i] = ' '; chars[i + 1] = ' ';
                        i += 2;
                        continue;
                    }
                    if (chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/')
                    {
                        depth--;
                        chars[i] = ' '; chars[i + 1] = ' ';
                        i += 2;
                        if (depth == 0) break;
                        continue;
                    }
                    if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
                    i++;
                }
                continue;
            }

            if (c == '\'')
            {
                chars[i++] = ' ';
                while (i < chars.Length)
                {
                    if (chars[i] == '\'')
                    {
                        if (i + 1 < chars.Length && chars[i + 1] == '\'')
                        {
                            chars[i] = ' '; chars[i + 1] = ' ';
                            i += 2;
                            continue;
                        }
                        chars[i++] = ' ';
                        break;
                    }
                    if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
                    i++;
                }
                continue;
            }

            if (c == '[')
            {
                while (i < chars.Length && chars[i] != ']') chars[i++] = ' ';
                if (i < chars.Length) chars[i++] = ' ';
                continue;
            }

            i++;
        }

        return new string(chars);
    }
}
