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
    public static void Lint(string text, List<SQuiLDiagnostic> diagnostics, string? squilFilePath = null)
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
        LintNullabilityHints(text, diagnostics);
        LintShapeMismatch(text, diagnostics);
        LintShapeCollision(text, diagnostics);
        LintSimilarSignatures(text, diagnostics);
        LintCardinalityCollision(text, diagnostics);
        LintUnmatchedSelect(text, diagnostics);
        if (squilFilePath is not null)
        {
            LintOrphanContext(squilFilePath, diagnostics);
            LintMutationDiagnostics(text, squilFilePath, diagnostics);
            LintDebugRollbackHint(text, squilFilePath, diagnostics);
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

    internal static void LintUnmatchedSelect(string sql, List<SQuiLDiagnostic> diagnostics)
    {
        var parsed = SQuiLParser.Parse(sql);

        var outputs = parsed.Variables
            .Where(v => (v.Role == VariableRole.Returns || v.Role == VariableRole.ReturnTable)
                        && v.Columns != null && v.Columns.Count > 0)
            .ToList();

        if (outputs.Count == 0) return;

        // Build the set of declared output column-name sequences (lower-cased).
        var declaredNameKeys = new HashSet<string>(
            outputs.Select(v => string.Join("|", v.Columns!.Select(c => c.Name.ToLowerInvariant()))));

        // Determine body start: everything after the USE statement line.
        if (parsed.DatabaseLine is not { } databaseLine) return;

        var allLines = sql.Split('\n');
        int bodyLineOffset = databaseLine + 1;

        for (int i = 0; i < allLines.Length - bodyLineOffset; i++)
        {
            string raw = allLines[bodyLineOffset + i];

            var selectMatch = SelectFromRegex.Match(raw);
            if (!selectMatch.Success) continue;             // only Select <list> From ...

            var cols = ExtractSelectColumnNames(selectMatch.Groups[1].Value);
            if (cols == null) continue;                     // not statically inferable -> skip

            string key = string.Join("|", cols.Select(c => c.ToLowerInvariant()));
            if (declaredNameKeys.Contains(key)) continue;

            string expected = string.Join(" | ", outputs.Select(v =>
                string.Join(", ", v.Columns!.Select(c => c.Name))));

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

    internal static void LintMutationDiagnostics(string sql, string squilFilePath, List<SQuiLDiagnostic> diagnostics)
    {
        var ctx = SQuiLContextResolver.Resolve(squilFilePath);
        if (!ctx.Found) return; // orphan/duplicate already reported by LintOrphanContext

        // Extract the body text: everything after the USE statement line.
        var parsed = SQuiLParser.Parse(sql);
        if (parsed.DatabaseLine is not { } databaseLine) return;

        var lines = sql.Split('\n');
        // Compute the character offset of the line after the USE statement.
        int bodyStartOffset = 0;
        for (int i = 0; i <= databaseLine && i < lines.Length; i++)
            bodyStartOffset += lines[i].Length + 1; // +1 for the '\n'

        var bodyText = bodyStartOffset < sql.Length ? sql.Substring(bodyStartOffset) : string.Empty;

        var scan = SQuiLMutationScanner.Scan(bodyText);

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
                // Try to locate the Begin Tran in the body for a precise range.
                var btMatch = System.Text.RegularExpressions.Regex.Match(
                    bodyText, @"\bBegin\s+Tran(?:saction)?\b",
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

    internal static void LintDebugRollbackHint(string sql, string squilFilePath, List<SQuiLDiagnostic> diagnostics)
    {
        var ctx = SQuiLContextResolver.Resolve(squilFilePath);
        if (!ctx.Found) return;
        if (ctx.Attribute != "SQuiLQueryTransaction") return;
        if (!ctx.Enabled) return;
        if (!ctx.DebugRollback) return;

        // Check whether @Debug is declared anywhere in the file.
        var parsed = SQuiLParser.Parse(sql);
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

    private static (int Line, int Char) OffsetToLineChar(string text, int offset)
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

    internal static void LintShapeMismatch(string sql, List<SQuiLDiagnostic> diagnostics)
    {
        var parsed = SQuiLParser.Parse(sql);
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

    internal static void LintCardinalityCollision(string sql, List<SQuiLDiagnostic> diagnostics)
    {
        var parsed = SQuiLParser.Parse(sql);

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

    internal static void LintShapeCollision(string sql, List<SQuiLDiagnostic> diagnostics)
    {
        var parsed = SQuiLParser.Parse(sql);

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

    internal static void LintSimilarSignatures(string sql, List<SQuiLDiagnostic> diagnostics)
    {
        var parsed = SQuiLParser.Parse(sql);
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

    internal static void LintNullabilityHints(string sql, List<SQuiLDiagnostic> diagnostics)
    {
        var parsed = SQuiLParser.Parse(sql);
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
                     && v.NullabilityMarker is null)
            {
                string csType = SqlTypeMap.SqlToCSharp(v.SqlType);
                diagnostics.Add(new SQuiLDiagnostic
                {
                    Message   = $"No `null`/`not null` marker — generated C# is non-nullable `{csType} {v.Name}`. "
                              + $"Add `not null` to confirm, or `null` to make it nullable.",
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
