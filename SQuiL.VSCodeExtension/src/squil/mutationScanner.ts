/**
 * SQuiL mutation scanner — TypeScript port of SQuiLMutationScanner.cs.
 * Classifies a SQL body string as provably-read-only or containing persistent mutations.
 *
 * Mirrors SQuiL.SourceGenerator.Parser.SQuiLMutationScanner — change one side, change the other.
 */

export interface MutationHit {
  kind: string;   // Insert | Update | Delete | Merge | Truncate | SelectInto | Exec
  start: number;
  length: number;
}

export interface MutationScanResult {
  isProvablyReadOnly: boolean;
  hasOwnTransaction: boolean;
  mutations: MutationHit[];
}

// Matches "Begin Tran" or "Begin Transaction" (case-insensitive).
const BEGIN_TRAN = /\bBegin\s+Tran(?:saction)?\b/gi;

// Matches DML keyword phrases followed by optional whitespace.
// We then inspect the original string at the target position.
const DML = /\b(Insert\s+Into|Update|Delete\s+From|Delete|Merge(?:\s+Into)?|Truncate\s+Table)\s+/gi;

// An @-prefixed table-variable target at position 0.
const AT_TARGET = /^@[A-Za-z_]\w*/;

// "Select … Into " — the target immediately follows.
const SELECT_INTO = /\bSelect\b[\s\S]*?\bInto\s+/gi;

// Exec / Execute keyword.
const EXEC = /\b(?:Exec|Execute)\b/gi;

/**
 * Replaces comments (line and nested block), string literals, and bracketed
 * identifiers with spaces so the scanner never sees their contents. Offsets
 * and newlines are preserved.
 *
 * Port of mask() from variableValidator.ts (originally ported from SQuiLLinter.cs MaskNonCode).
 */
function maskNonCode(sql: string): string {
  const chars = sql.split('');
  let i = 0;

  while (i < chars.length) {
    const c = chars[i];

    if (c === '-' && i + 1 < chars.length && chars[i + 1] === '-') {
      while (i < chars.length && chars[i] !== '\n') chars[i++] = ' ';
      continue;
    }

    if (c === '/' && i + 1 < chars.length && chars[i + 1] === '*') {
      let depth = 0;
      while (i < chars.length) {
        if (chars[i] === '/' && i + 1 < chars.length && chars[i + 1] === '*') {
          depth++;
          chars[i] = ' '; chars[i + 1] = ' ';
          i += 2;
          continue;
        }
        if (chars[i] === '*' && i + 1 < chars.length && chars[i + 1] === '/') {
          depth--;
          chars[i] = ' '; chars[i + 1] = ' ';
          i += 2;
          if (depth === 0) break;
          continue;
        }
        if (chars[i] !== '\n' && chars[i] !== '\r') chars[i] = ' ';
        i++;
      }
      continue;
    }

    if (c === "'") {
      chars[i++] = ' ';
      while (i < chars.length) {
        if (chars[i] === "'") {
          if (i + 1 < chars.length && chars[i + 1] === "'") {
            chars[i] = ' '; chars[i + 1] = ' ';
            i += 2;
            continue;
          }
          chars[i++] = ' ';
          break;
        }
        if (chars[i] !== '\n' && chars[i] !== '\r') chars[i] = ' ';
        i++;
      }
      continue;
    }

    if (c === '[') {
      while (i < chars.length && chars[i] !== ']') chars[i++] = ' ';
      if (i < chars.length) chars[i++] = ' ';
      continue;
    }

    i++;
  }

  return chars.join('');
}

/** Normalise a DML keyword phrase to a Kind string (Insert/Update/Delete/Merge/Truncate). */
function normaliseKind(kw: string): string {
  const first = kw.trimStart().split(/\s+/)[0].toLowerCase();
  if (first === 'truncate') return 'Truncate';
  if (first === 'merge') return 'Merge';
  return first.charAt(0).toUpperCase() + first.slice(1);
}

export function scanMutations(body: string): MutationScanResult {
  const masked = maskNonCode(body);
  const hits: MutationHit[] = [];

  // Reset regex lastIndex before each scan (global regexes are stateful).
  DML.lastIndex = 0;
  SELECT_INTO.lastIndex = 0;
  EXEC.lastIndex = 0;
  BEGIN_TRAN.lastIndex = 0;

  let m: RegExpExecArray | null;

  while ((m = DML.exec(masked)) !== null) {
    const targetPos = m.index + m[0].length;
    const originalAtTarget = targetPos < body.length && AT_TARGET.test(body.slice(targetPos));
    if (originalAtTarget) continue;

    const kind = normaliseKind(m[1]);
    hits.push({ kind, start: m.index, length: m[0].length });
  }

  while ((m = SELECT_INTO.exec(masked)) !== null) {
    const targetPos = m.index + m[0].length;
    const originalAtTarget = targetPos < body.length && AT_TARGET.test(body.slice(targetPos));
    if (!originalAtTarget) {
      hits.push({ kind: 'SelectInto', start: m.index, length: m[0].length });
    }
  }

  while ((m = EXEC.exec(masked)) !== null) {
    hits.push({ kind: 'Exec', start: m.index, length: m[0].length });
  }

  BEGIN_TRAN.lastIndex = 0;
  const hasOwnTransaction = BEGIN_TRAN.test(masked);

  return {
    isProvablyReadOnly: hits.length === 0,
    hasOwnTransaction,
    mutations: hits,
  };
}
