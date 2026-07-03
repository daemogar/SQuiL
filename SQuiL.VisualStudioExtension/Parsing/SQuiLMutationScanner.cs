using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SQuiL.VisualStudioExtension.Parsing;

public sealed class MutationHit
{
    public string Kind { get; }
    public int Start { get; }
    public int Length { get; }
    public MutationHit(string kind, int start, int length) { Kind = kind; Start = start; Length = length; }
}

public sealed class MutationScanResult
{
    public bool IsProvablyReadOnly { get; }
    public bool HasOwnTransaction { get; }
    public IReadOnlyList<MutationHit> Mutations { get; }
    public MutationScanResult(bool isProvablyReadOnly, bool hasOwnTransaction, IReadOnlyList<MutationHit> mutations)
    {
        IsProvablyReadOnly = isProvablyReadOnly;
        HasOwnTransaction = hasOwnTransaction;
        Mutations = mutations;
    }
}

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

    static readonly Regex SelectInto = new(
        @"\bSelect\b[\s\S]*?\bInto\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex Exec = new(
        @"\b(Exec|Execute)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MutationScanResult Scan(string body)
    {
        var masked = MaskNonCode(body);
        var hits = new List<MutationHit>();

        foreach (Match m in Dml.Matches(masked))
        {
            // The target token begins right after the matched keyword+whitespace.
            // If the original SQL at that position starts with '@', it's an @table-variable → read-only.
            var targetPos = m.Index + m.Length;
            var originalAtTarget = targetPos < body.Length
                && AtTarget.IsMatch(body.Substring(targetPos));
            if (originalAtTarget) continue;

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
            if (!originalAtTarget)
                hits.Add(new("SelectInto", m.Index, m.Length));
        }

        foreach (Match m in Exec.Matches(masked))
            hits.Add(new("Exec", m.Index, m.Length));

        return new(hits.Count == 0, BeginTran.IsMatch(masked), hits);
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
