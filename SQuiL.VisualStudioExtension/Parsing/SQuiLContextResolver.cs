using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SQuiL.VisualStudioExtension.Parsing;

/// <summary>
/// Resolves the C# <c>[SQuiLQuery]</c> or <c>[SQuiLQueryTransaction]</c> attribute
/// that registers a given <c>.squil</c> file, and reads the <c>enabled</c> /
/// <c>debugRollback</c> named arguments from it.
///
/// Resolution algorithm (mirrors <c>SQuiLGenerator.FlattenPath</c> +
/// <c>StripSqlExtension</c> exactly):
/// <list type="number">
///   <item>Compute the <c>QueryFiles</c> member name from the <c>.squil</c> path:
///     walk up to the nearest <c>.csproj</c>; take the relative path;
///     strip the extension; remove ALL separators (no case transform).</item>
///   <item>Scan every <c>.cs</c> file in the project tree for
///     <c>[SQuiLQuery(QueryFiles.&lt;member&gt;...)]</c> or
///     <c>[SQuiLQueryTransaction(QueryFiles.&lt;member&gt;...)]</c>.</item>
///   <item>Collect matches. 0 → SP0028 orphan warning; 1 → return facts;
///     &gt;1 → SP0027 duplicate error mirror.</item>
/// </list>
///
/// Port of <c>SQuiL.VSCodeExtension/src/squil/contextResolver.ts</c> —
/// change one side, change all.
/// </summary>
internal static class SQuiLContextResolver
{
    private static readonly string[] SqlExtensions = { ".squil", ".sql" };

    // ── Comment-stripping patterns ─────────────────────────────────────────
    private static readonly Regex BlockComment =
        new Regex(@"/\*[\s\S]*?\*/",                       RegexOptions.Compiled);
    private static readonly Regex LineComment =
        new Regex(@"//[^\r\n]*",                           RegexOptions.Compiled);

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Result of resolving a <c>.squil</c> file's C# attribute context.</summary>
    public sealed class ResolvedContext
    {
        public bool   Found        { get; }
        public int    MatchCount   { get; }
        /// <summary>"SQuiLQuery" | "SQuiLQueryTransaction" | null when MatchCount != 1.</summary>
        public string? Attribute   { get; }
        public bool   Enabled      { get; }
        public bool   DebugRollback { get; }

        internal ResolvedContext(bool found, int matchCount, string? attribute, bool enabled, bool debugRollback)
        {
            Found        = found;
            MatchCount   = matchCount;
            Attribute    = attribute;
            Enabled      = enabled;
            DebugRollback = debugRollback;
        }
    }

    /// <summary>
    /// Resolve the C# context attribute for the given <c>.squil</c> file using
    /// the real filesystem.
    /// </summary>
    public static ResolvedContext Resolve(string squilPath)
        => Resolve(squilPath,
            p => { try { return File.ReadAllText(p); } catch { return null; } },
            d => { try { return Directory.GetFileSystemEntries(d); } catch { return Array.Empty<string>(); } });

    /// <summary>
    /// Resolve with injected filesystem callbacks (for testing).
    /// </summary>
    /// <param name="squilPath">Absolute path to the .squil or .sql file.</param>
    /// <param name="readFile">Read a file; return null on error.</param>
    /// <param name="listDir">Return all direct-child full paths in a directory.</param>
    public static ResolvedContext Resolve(
        string squilPath,
        Func<string, string?> readFile,
        Func<string, string[]> listDir)
    {
        var notFound = new ResolvedContext(false, 0, null, false, true);

        // ── Locate the .csproj root ──────────────────────────────────────────
        string? csprojDir = FindCsprojDir(squilPath, listDir);
        if (csprojDir is null) return notFound;

        // ── Compute QueryFiles member name ───────────────────────────────────
        string member = ComputeMember(squilPath, csprojDir);
        if (string.IsNullOrEmpty(member)) return notFound;

        // ── Scan .cs files ───────────────────────────────────────────────────
        var matches = new List<CsMatch>();
        ScanDir(csprojDir, member, readFile, listDir, matches);

        int matchCount = matches.Count;

        if (matchCount == 0)
            return new ResolvedContext(false, 0, null, false, true);

        if (matchCount > 1)
            return new ResolvedContext(false, matchCount, null, false, true);

        var m = matches[0];
        return new ResolvedContext(true, 1, m.Attribute, m.Enabled, m.DebugRollback);
    }

    // ── Internal types ────────────────────────────────────────────────────

    private sealed class CsMatch
    {
        public string Attribute   { get; }
        public bool   Enabled     { get; }
        public bool   DebugRollback { get; }

        public CsMatch(string attribute, bool enabled, bool debugRollback)
        {
            Attribute    = attribute;
            Enabled      = enabled;
            DebugRollback = debugRollback;
        }
    }

    // ── Member-name computation ───────────────────────────────────────────

    /// <summary>
    /// Mirrors <c>SQuiLGenerator.StripSqlExtension(FlattenPath(relativePath))</c>.
    /// </summary>
    private static string ComputeMember(string squilPath, string csprojDir)
    {
        // Normalise separators → forward slash.
        string sq   = squilPath.Replace('\\', '/');
        string root = csprojDir.Replace('\\', '/').TrimEnd('/') + "/";

        string rel = sq.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? sq.Substring(root.Length)
            : sq;
        rel = rel.TrimStart('/', '\\');

        return StripSqlExtension(FlattenPath(rel));
    }

    private static string FlattenPath(string path)
        => path.Replace("\\", "").Replace("/", "");

    private static string StripSqlExtension(string path)
    {
        string lower = path.ToLowerInvariant();
        foreach (string ext in SqlExtensions)
            if (lower.EndsWith(ext))
                return path.Substring(0, path.Length - ext.Length);
        return path;
    }

    // ── .csproj locator ────────────────────────────────────────────────────

    // KNOWN LIMITATION — root-directory divergence:
    // The source generator computes the flatten-root as the longest common prefix
    // of the directories of ALL compiled .cs source files (sorted by length desc).
    // This resolver approximates that by using the .csproj directory instead.
    // For the standard layout (context .cs files at the project root) the two are
    // equivalent. If ALL context .cs files live in a subdirectory the computed
    // member name will diverge from the generator's value and resolution silently
    // fails (returns not-found / orphan).
    private static string? FindCsprojDir(string squilPath, Func<string, string[]> listDir)
    {
        string? dir = Path.GetDirectoryName(squilPath)?.Replace('\\', '/');

        for (int depth = 0; depth < 32 && dir is not null; depth++)
        {
            string[] entries;
            try { entries = listDir(dir); } catch { entries = Array.Empty<string>(); }

            foreach (string e in entries)
            {
                if (Path.GetFileName(e).EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    return dir;
            }

            string? parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            if (parent is null || parent == dir) break;
            dir = parent;
        }

        return null;
    }

    // ── .cs file scanner ─────────────────────────────────────────────────

    private static void ScanDir(
        string dir,
        string member,
        Func<string, string?> readFile,
        Func<string, string[]> listDir,
        List<CsMatch> results)
    {
        string[] entries;
        try { entries = listDir(dir); } catch { return; }

        foreach (string fullPath in entries)
        {
            string name = Path.GetFileName(fullPath);

            if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                string? text;
                try { text = readFile(fullPath); } catch { text = null; }
                if (text is not null)
                    CollectMatches(text, member, results);
            }
            else if (!name.Contains("."))
            {
                // Likely a subdirectory — recurse.
                // NOTE: this heuristic silently skips directories whose names contain a
                // dot (e.g. "SQuiL.Queries/"). Standard project query folders are
                // unaffected; only dotted directory names would be missed.
                ScanDir(fullPath, member, readFile, listDir, results);
            }
        }
    }

    // ── Comment masking ────────────────────────────────────────────────────

    /// <summary>
    /// Replace C# block and line comments with spaces so that attribute regexes
    /// never match inside commented-out code. Character offsets are preserved.
    /// </summary>
    private static string MaskComments(string src)
    {
        // Block comments first (may span lines), then line comments.
        string masked = BlockComment.Replace(src, m => new string(' ', m.Length));
        masked = LineComment.Replace(masked, m => new string(' ', m.Length));
        return masked;
    }

    private static void CollectMatches(string text, string member, List<CsMatch> results)
    {
        // Strip comments before scanning so commented-out attributes are ignored.
        string src = MaskComments(text);

        // Build a per-call pattern targeting the exact member name.
        var pattern = new Regex(
            @"\[SQuiLQuery(Transaction)?\s*\([^\]]*QueryFiles\." + Regex.Escape(member) + @"[^\]]*\]",
            RegexOptions.None);

        foreach (Match m in pattern.Matches(src))
        {
            bool isTxn     = m.Groups[1].Success;
            string attrText = m.Value;

            // Extract the args list between the outer parens.
            int argsStart = attrText.IndexOf('(');
            int argsEnd   = attrText.LastIndexOf(')');
            string argsText = (argsStart >= 0 && argsEnd > argsStart)
                ? attrText.Substring(argsStart + 1, argsEnd - argsStart - 1)
                : string.Empty;

            // Split at top-level commas; positional slots: 0=type, 1=setting, 2=enabled, 3=debugRollback.
            var args = SplitTopLevelArgs(argsText);
            bool enabled      = ParseBoolArg(args, "enabled",      2, isTxn ? true : false);
            bool debugRollback = ParseBoolArg(args, "debugRollback", 3, true);

            results.Add(new CsMatch(
                isTxn ? "SQuiLQueryTransaction" : "SQuiLQuery",
                enabled,
                debugRollback));
        }
    }

    /// <summary>
    /// Split an attribute arg list on top-level commas (ignoring commas inside nested parens).
    /// </summary>
    private static List<string> SplitTopLevelArgs(string argsText)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < argsText.Length; i++)
        {
            char c = argsText[i];
            if      (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(argsText.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        parts.Add(argsText.Substring(start).Trim());
        return parts;
    }

    /// <summary>
    /// Parse a bool attribute argument by named arg first, then positional slot.
    /// Named form: <c>argName: true</c> or <c>argName: false</c>.
    /// Positional: slot index among leading args that have no colon.
    /// </summary>
    private static bool ParseBoolArg(List<string> args, string argName, int positionalSlot, bool defaultValue)
    {
        // Try named arg (any position).
        var namedPattern = new Regex(@"^\s*" + Regex.Escape(argName) + @"\s*:\s*(true|false)\s*$",
            RegexOptions.IgnoreCase);
        foreach (string arg in args)
        {
            var m = namedPattern.Match(arg);
            if (m.Success)
                return string.Equals(m.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        // Collect leading positional args (stop at first named arg — C# requires positionals first).
        var positionals = new List<string>();
        var isNamed = new Regex(@"^\s*\w+\s*:");
        foreach (string arg in args)
        {
            if (isNamed.IsMatch(arg)) break;
            positionals.Add(arg.Trim());
        }

        if (positionalSlot < positionals.Count)
        {
            string val = positionals[positionalSlot];
            if (string.Equals(val, "true",  StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return defaultValue;
    }
}
