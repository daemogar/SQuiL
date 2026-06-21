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

  return result;
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

  const scalarNull = /\bnull\b/i.test(typeStr) && !/\bnot\s+null\b/i.test(typeStr);
  const scalarNotNull = /\bnot\s+null\b/i.test(typeStr);
  const scalarMarker: 'NULL' | 'NOT NULL' | undefined = scalarNull ? 'NULL' : scalarNotNull ? 'NOT NULL' : undefined;

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
