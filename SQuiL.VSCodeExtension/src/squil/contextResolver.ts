/**
 * SQuiL Editor Context Resolver
 *
 * Resolves the C# [SQuiLQuery] or [SQuiLQueryTransaction] attribute that
 * registers a given .squil file, and reads the `enabled` / `debugRollback`
 * named arguments from it.
 *
 * The resolution algorithm mirrors the source generator's member-naming:
 *   1. Compute the QueryFiles enum member from the .squil path:
 *      - Walk up to the nearest .csproj (repo root fallback).
 *      - Take the path relative to that directory.
 *      - Strip the .squil / .sql extension.
 *      - Remove ALL directory separators (\ and /).
 *      This is an exact port of SQuiLGenerator.FlattenPath + StripSqlExtension.
 *   2. Walk the project tree scanning every .cs file for
 *      [SQuiLQuery(QueryFiles.<member>...)] or
 *      [SQuiLQueryTransaction(QueryFiles.<member>...)]
 *   3. Collect matches with their attribute kind + named arg values.
 *
 * Injected filesystem callbacks make this unit-testable without a real disk.
 *
 * Port: SQuiL.SsmsExtension/Parsing/SQuiLContextResolver.cs and
 *       SQuiL.VisualStudioExtension/Parsing/SQuiLContextResolver.cs —
 *       change one side, change all.
 */

// ─── Public interface ─────────────────────────────────────────────────────

export interface ResolvedContext {
  /** true when exactly one .cs file registers this .squil file. */
  found: boolean;
  /** 0 = orphan (SP0028), 1 = OK, >1 = duplicate (SP0027). */
  matchCount: number;
  /** The attribute kind of the single matching registration (undefined when matchCount != 1). */
  attribute?: 'SQuiLQuery' | 'SQuiLQueryTransaction';
  /**
   * Whether a transaction wrapper is active.
   * - [SQuiLQueryTransaction] defaults to true.
   * - [SQuiLQuery] defaults to false (never wraps).
   * Falls back to false when not found (orphan/duplicate).
   */
  enabled: boolean;
  /**
   * Whether to roll back in debug mode.
   * Defaults to true for both attribute kinds.
   * Falls back to true when not found.
   */
  debugRollback: boolean;
}

/**
 * Resolve the C# context attribute for a given .squil file.
 *
 * @param squilPath   Absolute path to the .squil (or .sql) file.
 * @param readFile    Return file contents, or undefined if not readable.
 * @param listDir     Return the immediate child NAMES (not full paths) of a directory.
 */
export function resolveContext(
  squilPath: string,
  readFile: (p: string) => string | undefined,
  listDir: (d: string) => string[],
): ResolvedContext {
  const notFound: ResolvedContext = {
    found: false,
    matchCount: 0,
    enabled: false,
    debugRollback: true,
  };

  // ── Step 1: locate the .csproj root by walking up ────────────────────────
  const csprojDir = findCsprojDir(squilPath, readFile, listDir);
  if (csprojDir === undefined) return notFound;

  // ── Step 2: compute QueryFiles member name ───────────────────────────────
  const member = computeMember(squilPath, csprojDir);
  if (!member) return notFound;

  // ── Step 3: scan .cs files for [SQuiLQuery(QueryFiles.<member>...)] ──────
  const matches = scanCsFiles(csprojDir, member, readFile, listDir);

  const matchCount = matches.length;

  if (matchCount === 0) {
    return { found: false, matchCount: 0, enabled: false, debugRollback: true };
  }

  if (matchCount > 1) {
    return { found: false, matchCount, enabled: false, debugRollback: true };
  }

  const m = matches[0];
  return {
    found: true,
    matchCount: 1,
    attribute: m.attribute,
    enabled: m.enabled,
    debugRollback: m.debugRollback,
  };
}

// ─── Internal types ────────────────────────────────────────────────────────

interface CsMatch {
  attribute: 'SQuiLQuery' | 'SQuiLQueryTransaction';
  enabled: boolean;
  debugRollback: boolean;
}

// ─── Member-name computation (mirrors SQuiLGenerator.FlattenPath + StripSqlExtension) ──

const SQL_EXTENSIONS = ['.squil', '.sql'];

function stripSqlExtension(path: string): string {
  const lower = path.toLowerCase();
  for (const ext of SQL_EXTENSIONS) {
    if (lower.endsWith(ext)) {
      return path.slice(0, path.length - ext.length);
    }
  }
  return path;
}

function flattenPath(path: string): string {
  return path.replace(/\\/g, '').replace(/\//g, '');
}

/** Normalise path separators to forward slashes. */
function normSep(p: string): string {
  return p.replace(/\\/g, '/');
}

function computeMember(squilPath: string, csprojDir: string): string {
  const sq = normSep(squilPath);
  const root = normSep(csprojDir).replace(/\/$/, '') + '/';

  // Build relative path (strip the project root prefix).
  let rel = sq.startsWith(root) ? sq.slice(root.length) : sq;
  // Trim any leading separators.
  rel = rel.replace(/^[/\\]+/, '');

  return stripSqlExtension(flattenPath(rel));
}

// ─── .csproj root locator ──────────────────────────────────────────────────

/**
 * Walk from the .squil file's directory upward until a directory containing a
 * .csproj is found. Returns the directory path (with trailing slash stripped),
 * or undefined if none found before hitting the filesystem root.
 *
 * Uses `listDir` to enumerate each candidate directory — works in the
 * in-memory fake-fs without touching the real disk.
 *
 * KNOWN LIMITATION — root-directory divergence:
 * The source generator computes the flatten-root as the longest common prefix
 * of the directories of ALL compiled .cs source files (sorted by length desc).
 * This resolver approximates that by using the .csproj directory instead.
 * For the standard layout (context .cs files at the project root) the two are
 * equivalent. If ALL context .cs files live in a subdirectory the computed
 * member name will diverge from the generator's value and resolution silently
 * fails (returns not-found / orphan).
 */
function findCsprojDir(
  squilPath: string,
  _readFile: (p: string) => string | undefined,
  listDir: (d: string) => string[],
): string | undefined {
  // Start from the file's own directory.
  let dir = parentDir(normSep(squilPath));

  for (let depth = 0; depth < 32; depth++) {
    const entries = listDir(dir);
    if (entries.some(e => e.toLowerCase().endsWith('.csproj'))) {
      return dir;
    }

    const parent = parentDir(dir);
    if (parent === dir) break; // filesystem root — stop
    dir = parent;
  }

  return undefined;
}

function parentDir(p: string): string {
  const norm = normSep(p).replace(/\/$/, '');
  const slash = norm.lastIndexOf('/');
  if (slash <= 0) return '/';
  return norm.slice(0, slash);
}

// ─── .cs file scanner ─────────────────────────────────────────────────────

/**
 * Scan all .cs files under `rootDir` (recursively) for attribute usages that
 * reference `QueryFiles.<member>`. Returns all matches found.
 */
function scanCsFiles(
  rootDir: string,
  member: string,
  readFile: (p: string) => string | undefined,
  listDir: (d: string) => string[],
): CsMatch[] {
  const results: CsMatch[] = [];
  scanDir(rootDir, member, readFile, listDir, results);
  return results;
}

function scanDir(
  dir: string,
  member: string,
  readFile: (p: string) => string | undefined,
  listDir: (d: string) => string[],
  results: CsMatch[],
): void {
  const entries = listDir(dir);
  for (const entry of entries) {
    const fullPath = dir + '/' + entry;
    if (entry.toLowerCase().endsWith('.cs')) {
      const text = readFile(fullPath);
      if (text) {
        collectMatches(text, member, results);
      }
    } else if (!entry.includes('.')) {
      // Likely a directory — recurse. Skip entries with dots (files).
      // NOTE: this heuristic silently skips directories whose names contain a
      // dot (e.g. "SQuiL.Queries/"). Standard project query folders are
      // unaffected; only dotted directory names would be missed.
      scanDir(fullPath, member, readFile, listDir, results);
    }
  }
}

/**
 * Strip C# line comments (`// ...`) and block comments (`/* ... *\/`) from
 * source text, replacing them with whitespace so that character offsets are
 * preserved and attribute regex matches never land inside commented-out code.
 */
function maskComments(src: string): string {
  // Replace block comments first, then line comments.
  // Use a regex that handles multi-line block comments.
  let result = src.replace(/\/\*[\s\S]*?\*\//g, (m) => ' '.repeat(m.length));
  result = result.replace(/\/\/[^\r\n]*/g, (m) => ' '.repeat(m.length));
  return result;
}

/**
 * Scan `text` for attribute usages referencing `QueryFiles.<member>`.
 * Extracts `enabled` and `debugRollback` from named or positional args.
 *
 * Positional arg layout: (QueryFiles.X [slot 0], setting [slot 1], enabled [slot 2], debugRollback [slot 3])
 */
function collectMatches(text: string, member: string, results: CsMatch[]): void {
  // Strip comments before scanning so commented-out attributes are ignored.
  const src = maskComments(text);

  // Build a pattern that locates the attribute + member reference.
  // We look for: [SQuiLQuery(Transaction)? ... QueryFiles.<member> ... ]
  const memberPattern = new RegExp(
    `\\[SQuiLQuery(Transaction)?\\s*\\([^\\]]*QueryFiles\\.${escapeRegex(member)}[^\\]]*\\]`,
    'g',
  );

  let m: RegExpExecArray | null;
  while ((m = memberPattern.exec(src)) !== null) {
    const isTxn = m[1] === 'Transaction';
    const attrText = m[0];

    // Extract the args list text (between the outer parens).
    const argsStart = attrText.indexOf('(');
    const argsEnd = attrText.lastIndexOf(')');
    const argsText = argsStart >= 0 && argsEnd > argsStart
      ? attrText.slice(argsStart + 1, argsEnd)
      : '';

    // Split into individual args at top-level commas (respecting nested parens).
    const args = splitTopLevelArgs(argsText);

    // Parse positional slots: 0 = type, 1 = setting, 2 = enabled, 3 = debugRollback.
    const enabled = parseBoolArg(args, 'enabled', 2, isTxn ? true : false);
    const debugRollback = parseBoolArg(args, 'debugRollback', 3, true);

    results.push({
      attribute: isTxn ? 'SQuiLQueryTransaction' : 'SQuiLQuery',
      enabled,
      debugRollback,
    });
  }
}

/**
 * Split an attribute arg list on top-level commas (ignoring commas inside nested parens).
 * Returns trimmed arg strings.
 */
function splitTopLevelArgs(argsText: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < argsText.length; i++) {
    if (argsText[i] === '(') depth++;
    else if (argsText[i] === ')') depth--;
    else if (argsText[i] === ',' && depth === 0) {
      parts.push(argsText.slice(start, i).trim());
      start = i + 1;
    }
  }
  parts.push(argsText.slice(start).trim());
  return parts;
}

/**
 * Parse a bool attribute argument by name first, then by positional slot.
 * A named arg looks like `name: true` or `name: false`.
 * A positional arg is `true` or `false` without a colon prefix.
 */
function parseBoolArg(
  args: string[],
  argName: string,
  positionalSlot: number,
  defaultValue: boolean,
): boolean {
  // Try named arg (any position).
  for (const arg of args) {
    const namedMatch = arg.match(new RegExp(`^\\s*${argName}\\s*:\\s*(true|false)\\s*$`, 'i'));
    if (namedMatch) return namedMatch[1].toLowerCase() === 'true';
  }

  // Collect leading positional args (no colon = not named).
  // Stop at the first named arg to respect C# positional-before-named rule.
  const positionals: string[] = [];
  for (const arg of args) {
    if (/^\s*\w+\s*:/.test(arg)) break; // named arg — stop collecting positionals
    positionals.push(arg.trim());
  }

  if (positionalSlot < positionals.length) {
    const val = positionals[positionalSlot].toLowerCase();
    if (val === 'true') return true;
    if (val === 'false') return false;
  }

  return defaultValue;
}

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
