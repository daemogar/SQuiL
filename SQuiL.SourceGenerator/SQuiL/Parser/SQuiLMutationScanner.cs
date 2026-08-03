using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SQuiL.SourceGenerator.Parser;

public sealed record MutationHit(string Kind, int Start, int Length);

public sealed record MutationScanResult(
    bool IsProvablyReadOnly, bool HasOwnTransaction, IReadOnlyList<MutationHit> Mutations);

public static class SQuiLMutationScanner
{
    static readonly Regex BeginTran = new(
        @"\bBegin\s+Tran(saction)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches the DML statement keyword (and optional preposition) at the start of a statement.
    // We then inspect what follows in the masked string to decide read-only vs. mutation.
    // Group "kw" = the full keyword phrase.
    static readonly Regex Dml = new(
        @"\b(?<kw>Insert\s+Into|Update|Delete\s+From|Delete|Merge(?:\s+Into)?|Truncate\s+Table)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // An @-prefixed variable target at the position immediately following the keyword whitespace.
    static readonly Regex AtTarget = new(@"^@[A-Za-z_]\w*", RegexOptions.Compiled);

    // A bare identifier target at the position immediately following the keyword whitespace.
    // Used to recognise a DML target that names one of the query's OWN declared SQLite temp
    // tables (Create Temp Table Returns_X / Params_X / Return_X). SQLite has no @-prefix, so the
    // AtTarget skip cannot catch it — this is the SQLite analogue of T-SQL's @table-variable skip.
    static readonly Regex BareTarget = new(@"^[A-Za-z_]\w*", RegexOptions.Compiled);

    static readonly Regex SelectInto = new(
        @"\bSelect\b[\s\S]*?\bInto\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex Exec = new(
        @"\b(Exec|Execute)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="declaredTables">
    /// The query's OWN declared SQLite temp-table names (the full, unstripped physical names —
    /// e.g. <c>Returns_Imported</c>, <c>Params_Person</c>, <c>Return_Total</c>). A DML statement
    /// targeting one of these is NON-persistent — the SQLite analogue of a T-SQL @table-variable —
    /// so it must not raise SP0023. <c>null</c>/empty (every SQL Server query) preserves the
    /// original behaviour exactly.
    /// </param>
    public static MutationScanResult Scan(string body, IReadOnlyCollection<string>? declaredTables = null)
    {
        var masked = MaskNonCode(body);
        var hits = new List<MutationHit>();

        var declared = declaredTables is { Count: > 0 }
            ? new HashSet<string>(declaredTables, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (Match m in Dml.Matches(masked))
        {
            // The target token begins right after the matched keyword+whitespace.
            // If the original SQL at that position starts with '@', it's an @table-variable → read-only.
            var targetPos = m.Index + m.Length;
            var originalAtTarget = targetPos < body.Length
                && AtTarget.IsMatch(body.Substring(targetPos));
            if (originalAtTarget) continue;

            // SQLite: a target that names one of the query's own declared temp tables is
            // non-persistent (like a T-SQL @table-variable) → not a real-table mutation.
            if (IsDeclaredTarget(body, targetPos, declared)) continue;

            // Normalise keyword: first word of "kw" group, title-cased
            var kwRaw = m.Groups["kw"].Value;
            var firstWord = kwRaw.TrimStart();
            var spaceIdx = firstWord.IndexOf(' ');
            if (spaceIdx >= 0) firstWord = firstWord.Substring(0, spaceIdx);

            string kind;
            if (string.Equals(firstWord, "Truncate", StringComparison.OrdinalIgnoreCase))
                kind = "Truncate";
            else if (string.Equals(firstWord, "Merge", StringComparison.OrdinalIgnoreCase))
                kind = "Merge";
            else
                kind = char.ToUpper(firstWord[0]) + firstWord.Substring(1).ToLower();

            hits.Add(new(kind, m.Index, m.Length));
        }

        foreach (Match m in SelectInto.Matches(masked))
        {
            // The target token begins right after the matched "Select … Into " span.
            var targetPos = m.Index + m.Length;
            var originalAtTarget = targetPos < body.Length
                && AtTarget.IsMatch(body.Substring(targetPos));
            if (!originalAtTarget && !IsDeclaredTarget(body, targetPos, declared))
                hits.Add(new("SelectInto", m.Index, m.Length));
        }

        foreach (Match m in Exec.Matches(masked))
            hits.Add(new("Exec", m.Index, m.Length));

        return new(hits.Count == 0, BeginTran.IsMatch(masked), hits);
    }

    /// <summary>
    /// <c>true</c> when the bare identifier at <paramref name="targetPos"/> in the original SQL
    /// names one of the query's own declared SQLite temp tables (<paramref name="declared"/>).
    /// </summary>
    private static bool IsDeclaredTarget(string body, int targetPos, HashSet<string>? declared)
    {
        if (declared is null || targetPos >= body.Length) return false;
        var match = BareTarget.Match(body.Substring(targetPos));
        return match.Success && declared.Contains(match.Value);
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
