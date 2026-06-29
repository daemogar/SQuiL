/**
 * SQuiL SQL File Parser
 *
 * Parses SQuiL-annotated SQL files to extract:
 *   - Query name  (from --Name: comment)
 *   - Database    (from USE statement)
 *   - Variables   (from DECLARE statements, classified by role)
 *   - Diagnostics (errors/warnings for linting)
 */

export type VariableRole =
  | 'param'           // @Param_Name     — input scalar
  | 'params'          // @Params_Name    — input table-valued (IEnumerable)
  | 'param-table'     // @Param_Name TABLE(...) — input object
  | 'return'          // @Return_Name    — output scalar
  | 'returns'         // @Returns_Name   — output table (IEnumerable)
  | 'return-table'    // @Return_Name TABLE(...) — output object
  | 'debug'           // @Debug — bool special, not emitted as an ordinary property
  | 'suppressDebug'   // @SuppressDebug — bool special, not emitted as an ordinary property
  | 'environmentName' // @EnvironmentName
  | 'asOfDate'        // @AsOfDate — nullable typed Request property
  | 'unknown';        // unrecognised — triggers a warning

export interface TableColumn {
  name: string;
  sqlType: string;
  nullable: boolean;
  /** Explicit nullability keyword from the column declaration, if present. */
  nullabilityMarker?: 'NULL' | 'NOT NULL';
  /** Raw `DEFAULT <literal>` value (string literals keep their single quotes), or undefined. */
  defaultValue?: string;
}

export interface SQuiLVariable {
  role: VariableRole;
  /** Raw token as it appears in SQL, e.g. "@Param_Name" */
  rawName: string;
  /** Extracted C#-style name, e.g. "Name" */
  name: string;
  /** SQL type string, e.g. "VARCHAR(100)" or "TABLE" */
  sqlType: string;
  /** Column definitions if this is a TABLE type */
  columns?: TableColumn[];
  /** Whether the scalar variable is nullable (true only when NULL is explicit) */
  nullable?: boolean;
  /** Explicit nullability keyword from the scalar declaration, if present. */
  nullabilityMarker?: 'NULL' | 'NOT NULL';
  line: number;
  character: number;
}

export interface SQuiLDiagnostic {
  message: string;
  line: number;
  startChar: number;
  endChar: number;
  severity: 'error' | 'warning' | 'info';
  /** SP-prefixed diagnostic code, e.g. "SP0017". */
  code?: string;
  /** Line of the first (related) declaration site for two-location diagnostics. */
  relatedLine?: number;
  relatedStartChar?: number;
  relatedEndChar?: number;
  relatedMessage?: string;
}

export interface SQuiLParseResult {
  /** Query name from --Name: annotation */
  queryName?: string;
  /** Database from USE statement */
  database?: string;
  databaseLine?: number;
  variables: SQuiLVariable[];
  diagnostics: SQuiLDiagnostic[];
}

import { validateVariables, findingMessage, findingSeverity } from './variableValidator';

/** Parse a full SQuiL SQL file text into a structured result. */
export function parseSQuiL(text: string): SQuiLParseResult {
  const lines = text.split('\n');
  const result: SQuiLParseResult = {
    variables: [],
    diagnostics: [],
  };

  let useCount = 0;

  for (let i = 0; i < lines.length; i++) {
    const rawLine = lines[i];
    const trimmed = rawLine.trim();

    // --Name: annotation (only meaningful at the top, but we check anywhere)
    if (!result.queryName) {
      const nameMatch = trimmed.match(/^--\s*Name:\s*(.+)$/i);
      if (nameMatch) {
        result.queryName = nameMatch[1].trim();
        continue;
      }
    }

    // Skip blank lines and pure comments
    if (!trimmed || trimmed.startsWith('--') || trimmed.startsWith('/*')) {
      continue;
    }

    // USE statement
    const useMatch = trimmed.match(/^USE\s+\[?(\w+)\]?\s*;?\s*$/i);
    if (useMatch) {
      useCount++;
      const usePos = rawLine.search(/USE/i);
      if (useCount > 1) {
        result.diagnostics.push({
          message: 'Multiple USE statements found. Only one is allowed per SQuiL file.',
          line: i,
          startChar: usePos >= 0 ? usePos : 0,
          endChar: rawLine.trimEnd().length,
          severity: 'error',
        });
      } else {
        result.database = useMatch[1];
        result.databaseLine = i;
      }
      continue;
    }

    // DECLARE statement — capture the variable name and everything after it
    // Handles multiline TABLE declarations by joining continuation if needed
    const declareMatch = trimmed.match(/^DECLARE\s+(@\w+)\s+([\s\S]*?)(?:;|$)/i);
    if (declareMatch) {
      const varName = declareMatch[1];
      let typeStr = declareMatch[2].trim();

      // If a TABLE type starts here but the closing ) is on a later line, collect it
      if (/^TABLE\s*\(/i.test(typeStr) && !typeStr.includes(')')) {
        let j = i + 1;
        while (j < lines.length && !lines[j].includes(')')) {
          typeStr += ' ' + lines[j].trim();
          j++;
        }
        if (j < lines.length) {
          typeStr += ' ' + lines[j].trim().replace(/;.*$/, '');
        }
      }

      parseVariable(varName, typeStr, i, rawLine, result, useCount > 0);
    }
  }

  // Missing USE warning
  if (useCount === 0) {
    result.diagnostics.push({
      message: 'No USE statement found. SQuiL requires a USE [DatabaseName]; statement.',
      line: 0,
      startChar: 0,
      endChar: 0,
      severity: 'warning',
    });
  }

  // Undeclared-variable / special-placement validation (SQuiL files must be
  // valid T-SQL: every @reference needs a textually-preceding DECLARE, and
  // @Debug/@EnvironmentName belong at the top of the header).
  for (const finding of validateVariables(text)) {
    result.diagnostics.push({
      message: findingMessage(finding),
      line: finding.line,
      startChar: finding.character,
      endChar: finding.character + finding.name.length,
      severity: findingSeverity(finding),
    });
  }

  // SP0017: shape-mismatch detection across same-file declarations.
  for (const d of lintShapeMismatch(result)) {
    result.diagnostics.push(d);
  }

  // SP0022: cardinality collision (same name, list + single object, same side).
  for (const d of lintCardinalityCollision(result)) {
    result.diagnostics.push(d);
  }

  return result;
}

/** SP0017 — within a single file, detect table variables that share the same base
 *  name (after stripping @Returns_/@Return_/@Params_/@Param_ prefixes) but declare
 *  different column shapes.  Emits the second declaration as the primary location and
 *  points the relatedInformation at the first.
 */
export function lintShapeMismatch(result: SQuiLParseResult): SQuiLDiagnostic[] {
  const diagnostics: SQuiLDiagnostic[] = [];

  const tableRoles = new Set<VariableRole>(['returns', 'return-table', 'params', 'param-table']);
  const tableVars = result.variables.filter(v => tableRoles.has(v.role) && v.columns && v.columns.length > 0);

  const seen = new Map<string, SQuiLVariable>(); // name (lower) → first variable

  for (const v of tableVars) {
    const key = v.name.toLowerCase();
    const sig = (v.columns ?? []).map(c => `${c.name}:${c.sqlType.replace(/\s*\([^)]*\)/, '').toLowerCase()}:${c.nullable}`).join('|');

    const first = seen.get(key);
    if (!first) {
      seen.set(key, v);
      continue;
    }

    const firstSig = (first.columns ?? []).map(c => `${c.name}:${c.sqlType.replace(/\s*\([^)]*\)/, '').toLowerCase()}:${c.nullable}`).join('|');
    if (sig === firstSig) continue;

    diagnostics.push({
      message:
        `All declarations that generate the record \`${v.name}\` must declare identical columns ` +
        `(same names, types, nullability, and order). ` +
        `Rename one of the variables or align the column lists.`,
      line: v.line,
      startChar: v.character,
      endChar: v.character + v.rawName.length,
      severity: 'error',
      code: 'SP0017',
      relatedLine: first.line,
      relatedStartChar: first.character,
      relatedEndChar: first.character + first.rawName.length,
      relatedMessage: 'first declared here',
    });
  }

  return diagnostics;
}

/** SP0022 — within one file, a base name declared as BOTH a table (list:
 *  @Params_/@Returns_) AND a single object (@Param_…table/@Return_…table) on the SAME
 *  side (both inputs → request, or both outputs → response) resolves to one
 *  request/response property; the generator keeps the first and silently drops the rest.
 *  Warns on the first declaration and errors on each subsequent one, linking the two.
 *  Same rule as SQuiLCardinalityValidator.cs (generator) and LintCardinalityCollision in
 *  SQuiLLinter.cs (SSMS + Visual Studio) — change one, change all.
 */
export function lintCardinalityCollision(result: SQuiLParseResult): SQuiLDiagnostic[] {
  const diagnostics: SQuiLDiagnostic[] = [];

  const listRoles = new Set<VariableRole>(['params', 'returns']);
  const objectRoles = new Set<VariableRole>(['param-table', 'return-table']);
  const isList = (v: SQuiLVariable) => listRoles.has(v.role);
  const isObject = (v: SQuiLVariable) => objectRoles.has(v.role);
  const kind = (v: SQuiLVariable) => (isList(v) ? 'a table' : 'a single object');

  const tableVars = result.variables.filter(v => isList(v) || isObject(v));

  // group by (side, name): outputs feed the response, inputs the request.
  const groups = new Map<string, SQuiLVariable[]>();
  for (const v of tableVars) {
    const isOutput = v.role === 'returns' || v.role === 'return-table';
    const key = `${isOutput ? 'out' : 'in'}:${v.name.toLowerCase()}`;
    const g = groups.get(key);
    if (g) { g.push(v); } else { groups.set(key, [v]); }
  }

  for (const group of groups.values()) {
    if (!group.some(isList) || !group.some(isObject)) continue;

    const first = group[0];
    // Only declarations whose cardinality DIFFERS from the winner are conflicts.
    // A same-cardinality duplicate (e.g. a second @Returns_X) is a plain dedup, not
    // a collision — exclude it so 3+ same-name groups flag only the mismatches.
    const conflicts = group.slice(1).filter(v => isList(v) !== isList(first));
    if (conflicts.length === 0) continue;

    // Warning on the first declaration (it wins; the conflicting ones are dropped).
    diagnostics.push({
      message:
        `\`${first.rawName}\` declares \`${first.name}\` as ${kind(first)}, but the same name is also declared with a different cardinality below. ` +
        `One cardinality wins and the other is silently dropped — rename one variable, or use the same cardinality for both.`,
      line: first.line,
      startChar: first.character,
      endChar: first.character + first.rawName.length,
      severity: 'warning',
      code: 'SP0022',
      relatedLine: conflicts[0].line,
      relatedStartChar: conflicts[0].character,
      relatedEndChar: conflicts[0].character + conflicts[0].rawName.length,
      relatedMessage: 'conflicting cardinality declared here',
    });

    // Error on each conflicting declaration (these are silently dropped today).
    for (const v of conflicts) {
      diagnostics.push({
        message:
          `\`${v.rawName}\` declares \`${v.name}\` as ${kind(v)}, but \`${first.rawName}\` already declares it as ${kind(first)} (line ${first.line + 1}). ` +
          `One cardinality wins and the other is silently dropped — rename one variable, or use the same cardinality for both.`,
        line: v.line,
        startChar: v.character,
        endChar: v.character + v.rawName.length,
        severity: 'error',
        code: 'SP0022',
        relatedLine: first.line,
        relatedStartChar: first.character,
        relatedEndChar: first.character + first.rawName.length,
        relatedMessage: 'first declared here',
      });
    }
  }

  return diagnostics;
}

// ─── Internal helpers ──────────────────────────────────────────────────────

function parseVariable(
  rawName: string,
  typeStr: string,
  lineNum: number,
  fullLine: string,
  result: SQuiLParseResult,
  afterUse: boolean,
): void {
  const varStart = fullLine.indexOf(rawName);
  const upper = rawName.toUpperCase();
  const isTable = /^TABLE\s*\(/i.test(typeStr);

  let role: VariableRole;
  let name: string;

  if (upper === '@DEBUG') {
    role = 'debug';
    name = 'Debug';
  } else if (upper === '@SUPPRESSDEBUG') {
    role = 'suppressDebug';
    name = 'SuppressDebug';
  } else if (upper === '@ENVIRONMENTNAME') {
    role = 'environmentName';
    name = 'EnvironmentName';
  } else if (upper === '@ASOFDATE') {
    role = 'asOfDate';
    name = 'AsOfDate';
  } else if (upper.startsWith('@PARAMS_')) {
    role = 'params';
    name = rawName.substring('@Params_'.length);
  } else if (upper.startsWith('@PARAM_')) {
    role = isTable ? 'param-table' : 'param';
    name = rawName.substring('@Param_'.length);
  } else if (upper.startsWith('@RETURNS_')) {
    role = 'returns';
    name = rawName.substring('@Returns_'.length);
  } else if (upper.startsWith('@RETURN_')) {
    role = isTable ? 'return-table' : 'return';
    name = rawName.substring('@Return_'.length);
  } else {
    role = 'unknown';
    name = rawName.substring(1);
    // Only I/O declarations (before the USE) must follow SQuiL naming.
    // After the USE, @-variables are ordinary T-SQL locals in the query body —
    // don't require the @Param_/@Return_ convention for them.
    if (!afterUse) {
      result.diagnostics.push({
        message:
          `Variable '${rawName}' doesn't follow SQuiL naming conventions. ` +
          `Expected: @Param_*, @Params_*, @Return_*, @Returns_*, @Debug, @SuppressDebug, @EnvironmentName, or @AsOfDate.`,
        line: lineNum,
        startChar: varStart >= 0 ? varStart : 0,
        endChar: varStart >= 0 ? varStart + rawName.length : rawName.length,
        severity: 'warning',
      });
    }
  }

  // Parse TABLE column definitions
  let columns: TableColumn[] | undefined;
  const tableMatch = typeStr.match(/TABLE\s*\((.+)\)/is);
  if (tableMatch) {
    columns = parseTableColumns(tableMatch[1]);
  }

  const scalarNull = !isTable && /\bnull\b/i.test(typeStr) && !/\bnot\s+null\b/i.test(typeStr);
  const scalarNotNull = !isTable && /\bnot\s+null\b/i.test(typeStr);
  const scalarMarker: 'NULL' | 'NOT NULL' | undefined = isTable ? undefined :
    (scalarNull ? 'NULL' : scalarNotNull ? 'NOT NULL' : undefined);

  result.variables.push({
    role,
    rawName,
    name,
    sqlType: isTable ? 'TABLE' : typeStr.replace(/;$/, '').trim(),
    columns,
    nullable: scalarMarker === 'NULL',
    nullabilityMarker: scalarMarker,
    line: lineNum,
    character: varStart >= 0 ? varStart : 0,
  });
}

function parseTableColumns(columnsStr: string): TableColumn[] {
  const cols: TableColumn[] = [];
  // Split on commas not inside parens (for types like DECIMAL(18,2))
  const parts = splitTopLevelCommas(columnsStr);
  for (const part of parts) {
    const trimmed = part.trim();
    const match = trimmed.match(/^(\w+)\s+([\w]+(?:\([^)]*\))?)\s*(NULL|NOT\s+NULL)?\s*(?:DEFAULT\s+('[^']*'|\S+))?$/i);
    if (match) {
      const nullability = (match[3] ?? '').toUpperCase().trim();
      const marker = nullability === 'NULL' ? 'NULL' : nullability === 'NOT NULL' ? 'NOT NULL' : undefined;
      cols.push({
        name: match[1],
        sqlType: match[2].trim(),
        nullable: marker === 'NULL',
        nullabilityMarker: marker,
        defaultValue: match[4],
      });
    }
  }
  return cols;
}

function splitTopLevelCommas(str: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < str.length; i++) {
    if (str[i] === '(') depth++;
    else if (str[i] === ')') depth--;
    else if (str[i] === ',' && depth === 0) {
      parts.push(str.slice(start, i));
      start = i + 1;
    }
  }
  parts.push(str.slice(start));
  return parts;
}

/** Returns a human-readable description of a variable role. */
export function describeRole(role: VariableRole): string {
  switch (role) {
    case 'param':         return 'Input scalar parameter';
    case 'params':        return 'Input table-valued parameter (IEnumerable<T>)';
    case 'param-table':   return 'Input object parameter (TABLE type)';
    case 'return':        return 'Output scalar variable';
    case 'returns':       return 'Output table (IEnumerable<T>)';
    case 'return-table':  return 'Output object (TABLE type)';
    case 'debug':         return 'Debug flag (bool on *Request when declared)';
    case 'suppressDebug': return 'Suppress auto-debug flag (bool on *Request when declared; requires @Debug)';
    case 'environmentName': return 'Environment name (not a C# parameter)';
    case 'asOfDate':      return 'Point-in-time value (nullable typed property on *Request)';
    case 'unknown':       return 'Unknown — does not match SQuiL naming convention';
  }
}
