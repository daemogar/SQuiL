using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SQuiL.SsmsExtension.Parsing;

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
    public static void Lint(string text, List<SQuiLDiagnostic> diagnostics)
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
        LintSimilarSignatures(text, diagnostics);
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

        var seen = new Dictionary<string, SQuiLVariable>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var v in tableVars)
        {
            string sig = string.Join("|", v.Columns!.Select(c => $"{c.Name}:{c.SqlType}:{c.Nullable}"));
            if (!seen.TryGetValue(v.Name, out var first))
            {
                seen[v.Name] = v;
                continue;
            }
            string firstSig = string.Join("|", first.Columns!.Select(c => $"{c.Name}:{c.SqlType}:{c.Nullable}"));
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

    // ── Similar-signature hints (SP0020) ────────────────────────────────────
    //
    // Emits an Info-level hint for every table/object variable that shares an
    // EXACT column signature (same names, types, nullability, and order) with
    // a DIFFERENTLY-named variable in the same file.  This is the complement
    // of LintShapeMismatch (SP0017), which fires on same-name + different shape.
    //
    // Trigger:  different name + same signature.
    // Silent:   same name (SP0017's domain), or no matching signature found.
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

        // Build signature → list of variables.
        var bySig = new Dictionary<string, List<SQuiLVariable>>();
        foreach (var v in tableVars)
        {
            string sig = string.Join("|", v.Columns!.Select(c =>
                $"{c.Name}:{c.SqlType.ToLowerInvariant()}:{(c.Nullable ? "N" : "NN")}"));
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
                    EndChar   = a.Character + a.Name.Length,
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
