/**
 * SQuiL variable validator.
 *
 * Validates @variable usage in a SQuiL file:
 *   - Every @variable reference must be preceded by a DECLARE for that exact
 *     name — the same rule SQL Server enforces at batch compile time ("Must
 *     declare the scalar variable"). SQuiL performs no name remapping: a file
 *     that references an undeclared variable is invalid SQL. This applies to
 *     every variable including @Debug and @EnvironmentName.
 *   - @Debug and @EnvironmentName must be declared before the USE statement
 *     (in the header), and preferably before any other declaration.
 *
 * Pure logic with no vscode dependencies so it is unit-tested in isolation.
 * Port of SQuiLVariableValidator.cs in the source generator; mirrored by
 * LintUndeclaredVariables in the SSMS and Visual Studio extensions.
 * Change one, change the others.
 */

export type FindingKind =
  | 'undeclared'         // referenced but never declared — error
  | 'usedBeforeDeclared' // referenced before its declaration — error
  | 'specialAfterUse'    // a special declared after USE — error
  | 'specialNotFirst'    // a special not first in header — warning
  | 'suppressDebugWithoutDebug'; // @SuppressDebug declared without @Debug — error

export interface Finding {
  kind: FindingKind;
  /** The variable, including the '@'. */
  name: string;
  /** 0-based line of the reference/declaration. */
  line: number;
  /** 0-based column of the reference/declaration. */
  character: number;
}

type State = 'normal' | 'expectVariable' | 'inType' | 'inDefault';

const STATEMENT_STARTERS = new Set([
  'SELECT', 'INSERT', 'UPDATE', 'DELETE', 'SET', 'IF', 'WHILE', 'BEGIN', 'END',
  'USE', 'DECLARE', 'EXEC', 'EXECUTE', 'WITH', 'MERGE', 'PRINT', 'RETURN',
  'CREATE', 'DROP', 'ALTER', 'TRUNCATE', 'GO',
]);

function isSpecial(name: string): boolean {
  const upper = name.toUpperCase();
  return upper === '@DEBUG' || upper === '@SUPPRESSDEBUG'
    || upper === '@ENVIRONMENTNAME' || upper === '@ASOFDATE';
}

function isNameChar(c: string): boolean {
  return /[A-Za-z0-9_$#]/.test(c);
}

function isWordStart(c: string): boolean {
  return /[A-Za-z_]/.test(c);
}

/** Scans `sql` and returns every rule violation in document order. */
export function validateVariables(sql: string): Finding[] {
  const text = mask(sql);

  const declarations: { name: string; offset: number }[] = [];
  const references: { name: string; offset: number }[] = [];
  let useOffset: number | undefined;

  let state: State = 'normal';
  let parenDepth = 0;
  let caseDepth = 0;
  let i = 0;

  while (i < text.length) {
    const c = text[i];

    if (c === '(') { parenDepth++; i++; continue; }
    if (c === ')') { if (parenDepth > 0) parenDepth--; i++; continue; }

    if (c === ';') {
      if (parenDepth === 0) { state = 'normal'; caseDepth = 0; }
      i++;
      continue;
    }

    if (c === ',') {
      if (parenDepth === 0 && (state === 'inType' || state === 'inDefault')) {
        state = 'expectVariable';
      }
      i++;
      continue;
    }

    if (c === '=') {
      if (parenDepth === 0 && state === 'inType') state = 'inDefault';
      i++;
      continue;
    }

    if (c === '@') {
      const start = i;
      i++;
      if (i < text.length && text[i] === '@') {
        // system variable (@@ROWCOUNT etc.) — skip the whole token
        i++;
        while (i < text.length && isNameChar(text[i])) i++;
        continue;
      }

      const nameStart = i;
      while (i < text.length && isNameChar(text[i])) i++;
      if (i === nameStart) continue; // a lone '@' is not a variable

      const name = text.substring(start, i);

      if (state === 'expectVariable') {
        declarations.push({ name, offset: start });
        state = 'inType';
      } else {
        references.push({ name, offset: start });
      }
      continue;
    }

    if (isWordStart(c)) {
      const start = i;
      while (i < text.length && isNameChar(text[i])) i++;
      const word = text.substring(start, i).toUpperCase();

      if (word === 'DECLARE') {
        state = 'expectVariable';
        continue;
      }

      if (state === 'normal' && useOffset === undefined && word === 'USE' && parenDepth === 0) {
        useOffset = start;
        continue;
      }

      // CASE…END pairs inside a default-value expression must not end the
      // declare statement when END is reached.
      if (state === 'inDefault' && word === 'CASE') {
        caseDepth++;
        continue;
      }
      if (state === 'inDefault' && caseDepth > 0 && word === 'END') {
        caseDepth--;
        continue;
      }

      if (parenDepth === 0
        && (state === 'inType' || state === 'inDefault')
        && STATEMENT_STARTERS.has(word)) {
        state = 'normal';
        caseDepth = 0;
        // no semicolon between the declare and the next statement —
        // re-read the word in normal state so DECLARE/USE chains work
        i = start;
      }
      continue;
    }

    i++;
  }

  const findings: { offset: number; finding: Finding }[] = [];

  for (const ref of references) {
    let declaredBefore = false;
    let declaredAnywhere = false;
    for (const decl of declarations) {
      if (decl.name.toUpperCase() !== ref.name.toUpperCase()) continue;
      declaredAnywhere = true;
      if (decl.offset < ref.offset) { declaredBefore = true; break; }
    }

    if (declaredBefore) continue;

    const { line, character } = position(sql, ref.offset);
    findings.push({
      offset: ref.offset,
      finding: {
        kind: declaredAnywhere ? 'usedBeforeDeclared' : 'undeclared',
        name: ref.name,
        line,
        character,
      },
    });
  }

  for (const decl of declarations) {
    if (!isSpecial(decl.name)) continue;

    if (useOffset !== undefined && decl.offset > useOffset) {
      const { line, character } = position(sql, decl.offset);
      findings.push({
        offset: decl.offset,
        finding: { kind: 'specialAfterUse', name: decl.name, line, character },
      });
      continue;
    }

    for (const other of declarations) {
      if (other.offset >= decl.offset || isSpecial(other.name)) continue;

      const { line, character } = position(sql, decl.offset);
      findings.push({
        offset: decl.offset,
        finding: { kind: 'specialNotFirst', name: decl.name, line, character },
      });
      break;
    }
  }

  // @SuppressDebug only has meaning alongside @Debug (it gates the auto-debug
  // expression). Declaring it without @Debug is an error — mirrors the
  // generator's SP0019 (SuppressDebugWithoutDebug finding).
  const hasDebug = declarations.some((d) => d.name.toUpperCase() === '@DEBUG');
  if (!hasDebug) {
    for (const decl of declarations) {
      if (decl.name.toUpperCase() !== '@SUPPRESSDEBUG') continue;
      const { line, character } = position(sql, decl.offset);
      findings.push({
        offset: decl.offset,
        finding: { kind: 'suppressDebugWithoutDebug', name: decl.name, line, character },
      });
    }
  }

  return findings.sort((a, b) => a.offset - b.offset).map((f) => f.finding);
}

/** Human-readable message for a finding, matching the generator's SP0013/SP0016 wording. */
export function findingMessage(finding: Finding): string {
  switch (finding.kind) {
    case 'undeclared':
      return `Variable '${finding.name}' is referenced but never declared. `
        + 'SQuiL files must be valid T-SQL — declare it before use.';
    case 'usedBeforeDeclared':
      return `Variable '${finding.name}' is referenced before its declaration. `
        + 'Move the Declare above the first use.';
    case 'specialAfterUse':
      return `'${finding.name}' must be declared before the Use statement.`;
    case 'specialNotFirst':
      return `'${finding.name}' should be declared at the top of the header, before other declarations.`;
    case 'suppressDebugWithoutDebug':
      return `'${finding.name}' may only be declared when '@Debug' is also declared in the same file.`;
  }
}

/** Severity for a finding: only the placement *preference* is a warning. */
export function findingSeverity(finding: Finding): 'error' | 'warning' {
  return finding.kind === 'specialNotFirst' ? 'warning' : 'error';
}

/**
 * Replaces comments (line and nested block), string literals, and bracketed
 * identifiers with spaces so the scanner never sees their contents. Offsets
 * and newlines are preserved.
 */
function mask(sql: string): string {
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

function position(sql: string, offset: number): { line: number; character: number } {
  let line = 0;
  let character = 0;
  for (let i = 0; i < offset && i < sql.length; i++) {
    if (sql[i] === '\n') { line++; character = 0; }
    else character++;
  }
  return { line, character };
}
