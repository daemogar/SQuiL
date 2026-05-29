using System.Collections.Generic;
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
    /// are style hints, not errors.
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
}
