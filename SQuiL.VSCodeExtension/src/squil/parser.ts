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
  /** `true` when the column was declared `PRIMARY KEY` — its name becomes the table's
   * relationship key for nested-object linking. */
  isPrimaryKey: boolean;
  /** 0-based source line of the column NAME token — multi-line-`table(...)`-precise. */
  line: number;
  /** 0-based source character of the column NAME token on its own line. */
  character: number;
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
  /** 0-based source character of the nullability marker keyword itself (same line as `line`),
   *  when `nullabilityMarker` is set — lets SP0037 squiggle the exact keyword. */
  nullabilityMarkerCharacter?: number;
  /** Length of the marker keyword text as written ("NULL" or "NOT NULL"), for the squiggle range. */
  nullabilityMarkerLength?: number;
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
  /**
   * SP0041 candidates found by scanning the FULL file text (populated by `parseSQuiL`,
   * which already has the text in scope) for a `Select` whose top-level column list is
   * 2+ declared output-scalar references. Not part of the public port-of-the-scanner
   * surface — internal plumbing so `lintMultiScalarSelect` can work from the parse result
   * alone with document-absolute positions (the scan covers the whole file, not just the
   * body after `Use`, mirroring ScalarSelectAliaser.cs scanning the whole emitted command
   * text). See `lintMultiScalarSelect`, below.
   */
  multiScalarSelects: { line: number; character: number; length: number; declaredNames: string[] }[];
}

import { validateVariables, findingMessage, findingSeverity } from './variableValidator';
import { shapeKeyOf } from './shapeKey';
import { buildKeyGraph, KeyGraphResult, OUTPUT_TABLE_ROLES, INPUT_TABLE_ROLES } from './keyGraph';
import { EditorDialect, isTempTableDialect } from './dialect';
export { isTempTableDialect };

/**
 * Parse a full SQuiL SQL file text into a structured result.
 *
 * When <c>dialect</c> is a temp-table-header dialect (<c>'sqlite'</c> or <c>'postgres'</c>)
 * the header model is that dialect family's native
 * <c>Create Temp Table &lt;Prefix&gt;_&lt;Name&gt; (...)</c> form (direction/cardinality carried
 * by the bare name, single-column singular collapsing to a scalar) instead of T-SQL
 * <c>Declare @...</c> / <c>Use</c> — mirroring the generator's Task-5 header parsing.
 * Defaults to <c>'sqlserver'</c> so every existing caller is unaffected.
 */
export function parseSQuiL(text: string, dialect: EditorDialect = 'sqlserver'): SQuiLParseResult {
  const lines = text.split('\n');
  const result: SQuiLParseResult = {
    variables: [],
    diagnostics: [],
    multiScalarSelects: [],
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

    // Temp-table-header model (Task 5, SQLite + PostgreSQL): `Create Temp Table
    // <Prefix>_<Name> ( ... )` is the declaration form (no `@`, no `Use`). Direction/
    // cardinality come from the bare name, exactly as the `@`-prefixed T-SQL form.
    // Body/sample-DML statements after the header simply match no declaration regex
    // and are ignored here (the editor model only needs the declarations for
    // hover/completion/diagnostics).
    if (isTempTableDialect(dialect)) {
      // The name may be bracket-quoted (`[Params_Foo]`, full #3 parity) — mirrors the
      // generator's `IdentifierRegex`, which recognizes and strips brackets on both the
      // declaration name and DML targets. Bracket-quoted alternative captures group 1;
      // bare-name alternative captures group 2 — exactly one of the two is set.
      const createMatch = trimmed.match(/^CREATE\s+TEMP\s+TABLE\s+(?:\[(\w+)\]|(\w+))\s*\((.*)$/is);
      if (createMatch) {
        const tableName = createMatch[1] ?? createMatch[2];
        // Collect the (possibly multi-line) column list until the paren depth returns to 0.
        let inner = createMatch[3];
        let depth = 1 + parenDepthDelta(inner);
        let j = i + 1;
        while (depth > 0 && j < lines.length) {
          const seg = lines[j];
          inner += '\n' + seg;
          depth += parenDepthDelta(seg);
          j++;
        }
        const closeIdx = inner.lastIndexOf(')');
        const columnsInner = (closeIdx >= 0 ? inner.slice(0, closeIdx) : inner).trim();

        parseSqliteCreateTable(tableName, columnsInner, i, rawLine, result, lines);
        continue;
      }
    }

    // DECLARE statement — capture the variable name and everything after it
    // Handles multiline TABLE declarations by joining continuation if needed
    const declareMatch = trimmed.match(/^DECLARE\s+(@\w+)\s+([\s\S]*?)(?:;|$)/i);
    if (declareMatch) {
      const varName = declareMatch[1];
      let typeStr = declareMatch[2].trim();

      // If a TABLE type starts here but the closing ) is on a later line, collect it.
      // Tracks paren DEPTH (not "does this line contain a )") so a column whose type
      // itself carries parens (varchar(50), decimal(18,2), …) on an earlier line
      // doesn't fool the join into stopping before the table's real closing paren —
      // that silently dropped every column declared after it. Mirrors the
      // depth-tracking `scanTableColumnPositions` below already uses correctly.
      if (/^TABLE\s*\(/i.test(typeStr)) {
        let depth = parenDepthDelta(typeStr);
        let j = i + 1;
        while (depth > 0 && j < lines.length) {
          const seg = lines[j].trim().replace(/;.*$/, '');
          typeStr += ' ' + seg;
          depth += parenDepthDelta(seg);
          j++;
        }
      }

      parseVariable(varName, typeStr, i, rawLine, result, useCount > 0, lines);
    }
  }

  // Missing USE warning — temp-table-header dialects (SQLite, PostgreSQL) have no USE
  // statement (their header is Create Temp Table), so this T-SQL-only requirement must
  // not fire for them.
  if (useCount === 0 && !isTempTableDialect(dialect)) {
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

  // SP0032: timestamp/rowversion is server-generated and read-only; forbidden as an input.
  for (const d of lintTimestampInput(result)) {
    result.diagnostics.push(d);
  }

  // SP0037: a standalone null/not null marker on a scalar Declare is invalid T-SQL.
  for (const d of lintScalarNullMarker(result)) {
    result.diagnostics.push(d);
  }

  // SP0033 / SP0034: nested-object key-graph errors (ambiguous parent / cycle),
  // over BOTH the OUTPUT and INPUT graphs. SP0036: unsupported nested-input key type.
  for (const d of lintKeyGraph(result)) {
    result.diagnostics.push(d);
  }

  // SP0040: every @Param/@Params (input) must be declared before any @Return/@Returns
  // (output). Error for temp-table-header dialects (SQLite, PostgreSQL), warning
  // otherwise — severity follows the resolved dialect.
  for (const d of lintParamsBeforeReturns(result, dialect)) {
    result.diagnostics.push(d);
  }

  // SP0041 support data: scan the FULL file text (not just the body) for a Select whose
  // top-level column list is 2+ declared output-scalar references. Stored on the result
  // rather than pushed straight into result.diagnostics — lintMultiScalarSelect (an
  // on-demand pass, like lintShapeCollision/lintUnmatchedSelect) is what turns this into
  // SP0041 diagnostics, so it isn't double-emitted by both the automatic pass above and
  // an explicit call site.
  result.multiScalarSelects = findMultiScalarSelects(text, buildScalarsByVariableName(result.variables))
    .map(m => {
      const pos = offsetToPosition(text, m.selectOffset);
      return { line: pos.line, character: pos.character, length: 'select'.length, declaredNames: m.declaredNames };
    });

  return result;
}

/** SP0040 — within one file, an @Return/@Returns (output) declaration precedes a
 *  @Param/@Params (input) declaration. Inputs must be declared first. Reported once,
 *  anchored at the first offending output (the earliest output still followed by a later
 *  input). Severity is dialect-dependent: `error` for every temp-table-header dialect
 *  (SQLite, Postgres — their Create-Temp-Table header must create inputs before the shred
 *  reads them), `warning` otherwise. Same rule as SQuiLOrderingValidator.cs (generator) and
 *  LintParamsBeforeReturns in SQuiLLinter.cs (SSMS + Visual Studio) — change one, change all.
 *
 *  Generalized (Task 8) from a SQLite-only gate to the full temp-table family via
 *  isTempTableDialect(), matching the generator's FileGenerator.cs, which now gates on
 *  `dialect is ITempTableHeaderDialect`.
 */
export function lintParamsBeforeReturns(result: SQuiLParseResult, dialect: EditorDialect): SQuiLDiagnostic[] {
  const inputRoles = new Set<VariableRole>(['param', 'params', 'param-table']);
  const outputRoles = new Set<VariableRole>(['return', 'returns', 'return-table']);

  // Only INPUT/OUTPUT declarations participate, in file order. Specials/unknowns are skipped.
  const decls = result.variables.filter(v => inputRoles.has(v.role) || outputRoles.has(v.role));

  // Index of the last input; any output before it is out of order. No inputs → cannot violate.
  let lastInputIndex = -1;
  for (let i = 0; i < decls.length; i++) {
    if (inputRoles.has(decls[i].role)) lastInputIndex = i;
  }
  if (lastInputIndex < 0) return [];

  for (let i = 0; i < lastInputIndex; i++) {
    const v = decls[i];
    if (!outputRoles.has(v.role)) continue;
    return [{
      message:
        `\`${v.rawName}\` (an output) is declared before a later @Param/@Params input. ` +
        `Declare all @Param/@Params (inputs) before any @Return/@Returns (outputs).`,
      line: v.line,
      startChar: v.character,
      endChar: v.character + v.rawName.length,
      severity: isTempTableDialect(dialect) ? 'error' : 'warning',
      code: 'SP0040',
    }];
  }

  return [];
}

/**
 * SP0033 (Error) — a nested-object child's column matches the declared Primary
 * Key of more than one other table/object (ambiguous parent — a nested-object
 * child must resolve to exactly one parent).
 *
 * SP0034 (Error) — following Primary-Key/Foreign-Key links from a table
 * eventually returns to that same table (cycle — nested objects require a tree).
 *
 * Both are build errors in the generator (`SQuiLKeyGraph.Errors`,
 * `DiagnosticsMessages.ReportAmbiguousKeyLink` / `ReportKeyCycle`) — this is the
 * editor-squiggle mirror. Port of `LintKeyGraph` in `SQuiLLinter.cs`
 * (SSMS + Visual Studio) — change one side, change all three.
 *
 * Applied to BOTH the OUTPUT (`@Return_`/`@Returns_`) and INPUT (`@Param_`/
 * `@Params_`) key graphs, matching the generator building one of each
 * (`FileGenerator.cs`'s `keyGraph` / `inputGraph`). SP0036 (unsupported
 * nested-input key type) is checked against the INPUT graph only.
 */
export function lintKeyGraph(result: SQuiLParseResult): SQuiLDiagnostic[] {
  const diagnostics: SQuiLDiagnostic[] = [];
  const outputGraph = buildKeyGraph(result.variables, OUTPUT_TABLE_ROLES);
  const inputGraph = buildKeyGraph(result.variables, INPUT_TABLE_ROLES);

  for (const graph of [outputGraph, inputGraph]) {
    for (const finding of graph.errors) {
      const v = finding.variable;
      const other = finding.otherVariable;

      if (finding.kind === 'ambiguous') {
        diagnostics.push({
          message:
            `\`${v.name}\` (line ${v.line + 1}) links to more than one table — it also matches ` +
            `\`${other.name}\`'s (line ${other.line + 1}) primary key. A nested-object child must have ` +
            `exactly one parent — rename one of the key columns so only one match remains.`,
          line: v.line,
          startChar: v.character,
          endChar: v.character + v.rawName.length,
          severity: 'error',
          code: 'SP0033',
          relatedLine: other.line,
          relatedStartChar: other.character,
          relatedEndChar: other.character + other.rawName.length,
          relatedMessage: "matches this table's primary key",
        });
      } else {
        // cycle
        diagnostics.push({
          message:
            `\`${v.name}\` (line ${v.line + 1}) and \`${other.name}\` (line ${other.line + 1}) ` +
            `form a primary-key/foreign-key cycle. Nested objects cannot be recursive — remove one of the links.`,
          line: v.line,
          startChar: v.character,
          endChar: v.character + v.rawName.length,
          severity: 'error',
          code: 'SP0034',
          relatedLine: other.line,
          relatedStartChar: other.character,
          relatedEndChar: other.character + other.rawName.length,
          relatedMessage: 'cycle partner declared here',
        });
      }
    }
  }

  diagnostics.push(...lintUnsupportedInputKeyType(inputGraph));

  return diagnostics;
}

/** SQL types the generator can synthesize a nested-input join key for
 *  (`IsSynthesizableKeyType` in `FileGenerator.cs`): integer-family + uniqueidentifier. */
const SYNTHESIZABLE_KEY_TYPES = new Set(['int', 'bigint', 'smallint', 'uniqueidentifier']);

function baseSqlType(sqlType: string): string {
  return sqlType.replace(/\s*\([^)]*\)/, '').trim().split(/\s+/)[0]?.toLowerCase() ?? '';
}

/**
 * SP0036 (Error) — within the nested-INPUT key graph, a parent/child link
 * column's declared type is neither integer-family (int/bigint/smallint) nor
 * uniqueidentifier, so the generator cannot synthesize a join key for it.
 * Mirrors the build error (`IsSynthesizableKeyType` / `ReportUnsupportedKeyType`
 * in `FileGenerator.cs`/`DiagnosticsMessages.cs`) — editor-squiggle parity,
 * checked only against the INPUT graph (there is no INPUT-side equivalent on
 * OUTPUT graphs, which never synthesize keys).
 */
export function lintUnsupportedInputKeyType(inputGraph: KeyGraphResult): SQuiLDiagnostic[] {
  const diagnostics: SQuiLDiagnostic[] = [];

  for (const edge of inputGraph.edges) {
    const parentColumns = (edge.parent.columns ?? []) as TableColumn[];
    const keyColumn =
      parentColumns.find(c => c.isPrimaryKey && c.name.toLowerCase() === edge.keyName.toLowerCase()) ??
      parentColumns.find(c => c.name.toLowerCase() === edge.keyName.toLowerCase());
    if (!keyColumn) continue;
    if (SYNTHESIZABLE_KEY_TYPES.has(baseSqlType(keyColumn.sqlType))) continue;

    const child = edge.child;
    diagnostics.push({
      message:
        `Link column \`${edge.keyName}\` on \`${child.name}\` (line ${child.line + 1}) has type ` +
        `\`${keyColumn.sqlType}\`, which cannot have a join key synthesized. A nested-input key column must ` +
        `be an integer type (int, bigint, or smallint) or uniqueidentifier — change the link column's type.`,
      line: child.line,
      startChar: child.character,
      endChar: child.character + child.rawName.length,
      severity: 'error',
      code: 'SP0036',
    });
  }

  return diagnostics;
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
        `\`${first.rawName}\` declares \`${first.name}\` as ${kind(first)}, but \`${conflicts[0].rawName}\` (line ${conflicts[0].line + 1}) declares it as ${kind(conflicts[0])}. ` +
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

/** SP0032 — timestamp/rowversion is a server-generated, read-only value and cannot be a
 *  meaningful input. Flags any INPUT declaration (scalar @Param_/@Params_ or a column of
 *  an input table) whose SQL type is timestamp/rowversion. Output declarations are fine
 *  (byte[]). Same rule as SQuiLTimestampInputValidator.cs (generator) and
 *  LintTimestampInput in SQuiLLinter.cs (SSMS + Visual Studio) — change one, change all.
 */
export function lintTimestampInput(result: SQuiLParseResult): SQuiLDiagnostic[] {
  const diagnostics: SQuiLDiagnostic[] = [];

  const inputRoles = new Set<VariableRole>(['param', 'params', 'param-table']);
  const isTimestamp = (sqlType: string): boolean => {
    const base = sqlType.replace(/\s*\([^)]*\)/, '').trim().split(/\s+/)[0]?.toLowerCase();
    return base === 'timestamp' || base === 'rowversion';
  };

  for (const v of result.variables) {
    if (!inputRoles.has(v.role)) continue;

    if (v.columns && v.columns.length > 0) {
      for (const col of v.columns) {
        if (!isTimestamp(col.sqlType)) continue;
        diagnostics.push({
          message:
            `\`${v.name}.${col.name}\` is a timestamp/rowversion used as an input. ` +
            `timestamp is server-generated and read-only — use it only on @Return_/@Returns_ outputs, or remove it.`,
          line: col.line,
          startChar: col.character,
          endChar: col.character + col.name.length,
          severity: 'error',
          code: 'SP0032',
        });
      }
    } else if (isTimestamp(v.sqlType)) {
      diagnostics.push({
        message:
          `\`${v.name}\` is a timestamp/rowversion used as an input. ` +
          `timestamp is server-generated and read-only — use it only on @Return_/@Returns_ outputs, or remove it.`,
        line: v.line,
        startChar: v.character,
        endChar: v.character + v.rawName.length,
        severity: 'error',
        code: 'SP0032',
      });
    }
  }

  return diagnostics;
}

/** SP0037 — a standalone `null`/`not null` marker on a scalar Declare is invalid T-SQL
 *  (Declare doesn't support nullability modifiers). Use an `= null` initializer to make
 *  the scalar nullable instead — table/object column markers are unaffected (out of scope).
 *  Same rule as SQuiLScalarMarkerValidator.cs (generator) and LintScalarNullMarker in
 *  SQuiLLinter.cs (SSMS + Visual Studio) — change one, change all.
 */
export function lintScalarNullMarker(result: SQuiLParseResult): SQuiLDiagnostic[] {
  const diagnostics: SQuiLDiagnostic[] = [];

  for (const v of result.variables) {
    if (!v.nullabilityMarker) continue;

    const startChar = v.nullabilityMarkerCharacter ?? v.character;
    const length = v.nullabilityMarkerLength ?? v.rawName.length;

    diagnostics.push({
      message:
        `\`${v.rawName}\` has a \`null\`/\`not null\` marker, which is invalid T-SQL on a scalar Declare. ` +
        `Use \`= null\` to make it nullable, or remove the marker for non-nullable.`,
      line: v.line,
      startChar,
      endChar: startChar + length,
      severity: 'error',
      code: 'SP0037',
    });
  }

  return diagnostics;
}

/** SP0030 — within a single file, detect OUTPUT table variables (returns / return-table)
 *  that have DISTINCT names but IDENTICAL canonical shape keys (same column names, order,
 *  and C# types — length/precision does NOT differentiate).  When two or more outputs
 *  share a key, the runtime cannot route result sets to different records; all are flagged
 *  as errors with cross-referencing related-information.
 *
 *  Same-name is NOT a collision (same-name + different shape = SP0017's domain).
 *
 *  Port of SQuiLLinter.LintShapeCollision in SQuiLLinter.cs (SSMS + VS extensions) —
 *  change one, change all three.
 */
export function lintShapeCollision(parsed: SQuiLParseResult): SQuiLDiagnostic[] {
  const outputs = parsed.variables.filter(v => v.role === 'returns' || v.role === 'return-table');
  const groups = new Map<string, SQuiLVariable[]>();
  for (const v of outputs) {
    if (!v.columns) continue;
    const key = shapeKeyOf(v.columns);
    const existing = groups.get(key);
    if (existing) {
      existing.push(v);
    } else {
      groups.set(key, [v]);
    }
  }
  const diags: SQuiLDiagnostic[] = [];
  for (const group of groups.values()) {
    // Deduplicate by name (case-insensitive) — only distinct names are a collision.
    const distinct = group.filter(
      (v, i) => group.findIndex(g => g.name.toLowerCase() === v.name.toLowerCase()) === i,
    );
    if (distinct.length < 2) continue;
    const winner = distinct[0];
    for (let i = 0; i < distinct.length; i++) {
      const self = distinct[i];
      const other = i === 0 ? distinct[1] : winner;
      diags.push({
        message:
          `\`${self.rawName}\` has the same result signature as \`${other.rawName}\` ` +
          `(line ${other.line + 1}) — identical column names, order, and C# types ` +
          `(length/precision does not differentiate). Result sets can't be routed apart. ` +
          `Differentiate a column, or share one name.`,
        line: self.line,
        startChar: self.character,
        endChar: self.character + self.rawName.length,
        severity: 'error',
        code: 'SP0030',
        relatedLine: other.line,
        relatedStartChar: other.character,
        relatedEndChar: other.character + other.rawName.length,
        relatedMessage: 'conflicting result signature declared here',
      });
    }
  }
  return diags;
}

// ─── Internal helpers ──────────────────────────────────────────────────────

/**
 * SQLite header parser (Task 5): maps one `Create Temp Table <Prefix>_<Name> ( ... )`
 * statement to the SAME `SQuiLVariable`/`TableColumn` model the T-SQL `Declare @...` path
 * builds. Direction + cardinality come from the bare `<Prefix>_` (Params_/Param_/Returns_/
 * Return_); a SINGULAR (Param_/Return_) declaration with exactly one column collapses to a
 * scalar variable, mirroring the generator's single-column-object collapse.
 */
function parseSqliteCreateTable(
  tableName: string,
  columnsInner: string,
  lineNum: number,
  fullLine: string,
  result: SQuiLParseResult,
  allLines: string[],
): void {
  const nameStart = fullLine.indexOf(tableName);
  const character = nameStart >= 0 ? nameStart : 0;

  const underscore = tableName.indexOf('_');
  const prefix = (underscore >= 0 ? tableName.slice(0, underscore) : tableName).toUpperCase();
  const baseName = underscore >= 0 ? tableName.slice(underscore + 1) : tableName;

  const columns = parseTableColumns(columnsInner);
  const isPlural = prefix === 'PARAMS' || prefix === 'RETURNS';
  const isInput = prefix === 'PARAM' || prefix === 'PARAMS';
  const isOutput = prefix === 'RETURN' || prefix === 'RETURNS';

  // Single-column SINGULAR declaration collapses to a scalar (Param_ -> param, Return_ -> return).
  if (!isPlural && (isInput || isOutput) && columns.length === 1) {
    const col = columns[0];
    result.variables.push({
      role: isInput ? 'param' : 'return',
      rawName: tableName,
      name: baseName,
      sqlType: col.sqlType,
      nullable: col.nullabilityMarker === 'NULL',
      line: lineNum,
      character,
    });
    return;
  }

  let role: VariableRole;
  if (prefix === 'PARAMS') role = 'params';
  else if (prefix === 'PARAM') role = 'param-table';
  else if (prefix === 'RETURNS') role = 'returns';
  else if (prefix === 'RETURN') role = 'return-table';
  else role = 'unknown';

  // Precise per-column source positions: scan from this line using the SQLite header open
  // pattern (`Temp Table <name> (`) instead of the T-SQL `table(` pattern.
  const colPositions = scanTableColumnPositions(allLines, lineNum, 0, /\bTEMP\s+TABLE\s+\w+\s*\(/i);
  if (colPositions.length === columns.length) {
    columns.forEach((col, idx) => {
      col.line = colPositions[idx].line;
      col.character = colPositions[idx].character;
    });
  } else {
    for (const col of columns) {
      col.line = lineNum;
      col.character = character;
    }
  }

  result.variables.push({
    role,
    rawName: tableName,
    name: baseName,
    sqlType: 'TABLE',
    columns,
    line: lineNum,
    character,
  });
}

function parseVariable(
  rawName: string,
  typeStr: string,
  lineNum: number,
  fullLine: string,
  result: SQuiLParseResult,
  afterUse: boolean,
  allLines: string[],
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

    // Default fallback: the variable's own position (matches the old,
    // variable-precise-only behavior) — overwritten below when the
    // multi-line-aware scan finds precise per-column positions.
    const fallbackLine = lineNum;
    const fallbackChar = varStart >= 0 ? varStart : 0;
    for (const col of columns) {
      col.line = fallbackLine;
      col.character = fallbackChar;
    }

    const colPositions = scanTableColumnPositions(allLines, lineNum, fallbackChar + rawName.length);
    if (colPositions.length === columns.length) {
      columns.forEach((col, idx) => {
        col.line = colPositions[idx].line;
        col.character = colPositions[idx].character;
      });
    }
  }

  const eqIndex = typeStr.search(/=\s*/);
  const typeOnly = eqIndex >= 0 ? typeStr.slice(0, eqIndex) : typeStr;
  const initializer = eqIndex >= 0 ? typeStr.slice(eqIndex).replace(/^=\s*/, '') : '';

  const nullFromInitializer = !isTable && /^null\b/i.test(initializer);
  const scalarNull = !isTable && /\bnull\b/i.test(typeOnly) && !/\bnot\s+null\b/i.test(typeOnly);
  const scalarNotNull = !isTable && /\bnot\s+null\b/i.test(typeOnly);
  const scalarMarker: 'NULL' | 'NOT NULL' | undefined = isTable ? undefined :
    (scalarNull ? 'NULL' : scalarNotNull ? 'NOT NULL' : undefined);

  // Locate the marker keyword itself (for SP0037's squiggle range) by searching the raw
  // line starting just after the variable name — scalar DECLAREs are always single-line.
  let nullabilityMarkerCharacter: number | undefined;
  let nullabilityMarkerLength: number | undefined;
  if (scalarMarker) {
    const searchFrom = varStart >= 0 ? varStart + rawName.length : 0;
    const markerRegex = scalarMarker === 'NOT NULL' ? /\bnot\s+null\b/i : /\bnull\b/i;
    const match = fullLine.slice(searchFrom).match(markerRegex);
    if (match && match.index !== undefined) {
      nullabilityMarkerCharacter = searchFrom + match.index;
      nullabilityMarkerLength = match[0].length;
    }
  }

  result.variables.push({
    role,
    rawName,
    name,
    sqlType: isTable ? 'TABLE' : typeStr.replace(/;$/, '').trim(),
    columns,
    nullable: nullFromInitializer || scalarMarker === 'NULL',
    nullabilityMarker: scalarMarker,   // SP0037 flags any standalone marker as invalid T-SQL
    nullabilityMarkerCharacter,
    nullabilityMarkerLength,
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
    const head = trimmed.match(/^(\w+)\s+([\w]+(?:\([^)]*\))?)\s*(.*)$/is);
    if (!head) continue;

    let nullabilityMarker: 'NULL' | 'NOT NULL' | undefined;
    let isPrimaryKey = false;
    let defaultValue: string | undefined;

    // Peel optional column modifiers in any order: null marker, Primary Key,
    // default — mirrors the generator's tokenizer-driven peeling loop.
    let tail = head[3].trim();
    while (tail.length > 0) {
      const notNull = tail.match(/^NOT\s+NULL\b\s*/i);
      const nullOnly = notNull ? null : tail.match(/^NULL\b\s*/i);
      const primaryKey = notNull || nullOnly ? null : tail.match(/^PRIMARY\s+KEY\b\s*/i);
      const defaultMatch = notNull || nullOnly || primaryKey ? null : tail.match(/^DEFAULT\s+('[^']*'|\S+)\s*/i);

      if (notNull) {
        nullabilityMarker = 'NOT NULL';
        tail = tail.slice(notNull[0].length);
      } else if (nullOnly) {
        nullabilityMarker = 'NULL';
        tail = tail.slice(nullOnly[0].length);
      } else if (primaryKey) {
        isPrimaryKey = true;
        tail = tail.slice(primaryKey[0].length);
      } else if (defaultMatch) {
        defaultValue = defaultMatch[1];
        tail = tail.slice(defaultMatch[0].length);
      } else {
        break;
      }
    }

    cols.push({
      name: head[1],
      sqlType: head[2].trim(),
      nullable: nullabilityMarker === 'NULL',
      nullabilityMarker,
      defaultValue,
      isPrimaryKey,
      // Positions are filled in by the caller (parseVariable) once the
      // declare's real source location is known — placeholders here.
      line: 0,
      character: 0,
    });
  }
  return cols;
}

/**
 * Scans the ORIGINAL source lines (not the joined/trimmed `typeStr`) for a
 * `table( ... )` declaration starting at (startLine, startChar) and returns
 * the source (line, character) position of each top-level column NAME token,
 * in declaration order — correct even when the table spans multiple lines.
 *
 * Nested parens (e.g. `decimal(18,2)`) are tracked via paren depth so their
 * commas are never mistaken for column separators (only depth===1 commas
 * split columns).
 */
function scanTableColumnPositions(
  lines: string[],
  startLine: number,
  startChar: number,
  open: RegExp = /\bTABLE\s*\(/i,
): { line: number; character: number }[] {
  const results: { line: number; character: number }[] = [];

  // Flatten the source from (startLine, startChar) to EOF into one string,
  // with a parallel map from flattened index -> (line, character) in the
  // original source, so multi-line declarations still yield real positions.
  const flatChars: string[] = [];
  const map: { line: number; character: number }[] = [];
  for (let li = startLine; li < lines.length; li++) {
    const content = lines[li];
    const begin = li === startLine ? Math.min(Math.max(startChar, 0), content.length) : 0;
    for (let ci = begin; ci < content.length; ci++) {
      flatChars.push(content[ci]);
      map.push({ line: li, character: ci });
    }
    if (li < lines.length - 1) {
      flatChars.push('\n');
      map.push({ line: li, character: content.length });
    }
  }

  const text = flatChars.join('');
  const openMatch = open.exec(text);
  if (!openMatch) return results;

  const isNameChar = (c: string) => /[A-Za-z0-9_]/.test(c);

  let idx = openMatch.index + openMatch[0].length; // just past the opening '('
  let depth = 1;
  let atSegmentStart = true;

  while (idx < text.length && depth > 0) {
    if (atSegmentStart) {
      while (idx < text.length && /\s/.test(text[idx])) idx++;
      if (idx >= text.length) break;

      const nameStart = idx;
      while (idx < text.length && isNameChar(text[idx])) idx++;
      if (idx > nameStart) results.push(map[nameStart]);

      atSegmentStart = false;
      continue;
    }

    const c = text[idx];
    if (c === '(') { depth++; idx++; continue; }
    if (c === ')') { depth--; idx++; if (depth === 0) break; continue; }
    if (c === ',' && depth === 1) { atSegmentStart = true; idx++; continue; }
    idx++;
  }

  return results;
}

/**
 * Determine the 0-based line on which the query BODY begins for a SQLite (USE-less)
 * `.squil` file, mirroring the generator tokenizer's SQLite body boundary (Task 5):
 * the body is everything AFTER the leading `Create Temp Table` declarations and any
 * leading bare-name param-table population statements
 * (`Insert|Update|Delete <ParamTable> …`).
 *
 * SQLite files have no `USE`, so the T-SQL `databaseLine + 1` body derivation yields an
 * empty body and must NOT be used for them (that was the Task-9 review bug: the SP0025
 * SQLite `Begin` regex was dead and a legit SQLite mutation drew a spurious SP0024).
 * Reuses the already-parsed declaration set (`parsed.variables`) for the param-table
 * names — it does NOT re-parse from scratch.
 *
 * Returns `lines.length` when the file is header-only (no body).
 *
 * Mirrors `SQuiLParser.SqliteBodyStartLine` in SQuiLParser.cs (SSMS + Visual Studio) —
 * change one side, change all three.
 */
export function sqliteBodyStartLine(text: string, parsed: SQuiLParseResult): number {
  const lines = text.split('\n');

  // Param/Params-prefixed SQLite temp-table names (bare, no `@`) — the same set the
  // tokenizer's SqliteCreateTempTable records for sample-DML population recognition.
  const paramTableNames = new Set<string>(
    parsed.variables
      .filter(v => v.role === 'param' || v.role === 'params' || v.role === 'param-table')
      .map(v => v.rawName)
      .filter(n => !n.startsWith('@') && /^params?_/i.test(n))
      .map(n => n.toLowerCase()),
  );

  let i = 0;
  while (i < lines.length) {
    const trimmed = lines[i].trim();

    // Skip blank and comment-only lines.
    if (trimmed === '' || trimmed.startsWith('--') || trimmed.startsWith('/*')) { i++; continue; }

    // `Create Temp Table <name> ( … )` declaration — consume the (possibly multi-line)
    // statement until the column-list paren depth returns to 0. The name may be bracket-quoted
    // (`[Name]`, #3) and the opening `(` may be on a SUBSEQUENT line (#2 — matched by the `$`
    // alternative), so the depth-tracking loop below finds the real end regardless.
    if (/^CREATE\s+TEMP\s+TABLE\s+(?:\[\w+\]|\w+)\s*(?:\(|$)/i.test(trimmed)) {
      let depth = 0;
      let opened = false;
      while (i < lines.length) {
        depth += parenDepthDelta(lines[i]);
        if (lines[i].includes('(')) opened = true;
        i++;
        if (opened && depth <= 0) break;
      }
      continue;
    }

    // Bare-name param-table population DML (`Insert Into <ParamTable> …`, `Update <ParamTable> …`,
    // `Delete [From] <ParamTable> …`) — the SQLite analog of the T-SQL `Insert Into @Var …`
    // sample-data marker. Only skip when the target is a declared param table; DML against an
    // OUTPUT table or an ordinary real table is real body logic (the body begins there).
    // The target may be bracket-quoted (`[ParamTable]`, #3) — properly PAIRED (`\[(\w+)\]` or
    // `(\w+)`, not the looser `\[?(\w+)\]?` which would also match an unbalanced `[Foo` or
    // `Foo]`). Exactly one of the two capture groups is set; the membership comparison sees the
    // name bracket-stripped either way.
    const dml = trimmed.match(/^(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM|DELETE)\s+(?:\[(\w+)\]|(\w+))/i);
    if (dml && paramTableNames.has((dml[1] ?? dml[2]).toLowerCase())) {
      // Consume through the statement terminator `;`.
      while (i < lines.length && !lines[i].includes(';')) i++;
      if (i < lines.length) i++;
      continue;
    }

    // First statement that is neither a header declaration nor a param-table population
    // → the body begins here.
    return i;
  }

  return lines.length;
}

/** Net change in paren depth across a string ('(' count minus ')' count).
 *  Used to find the real end of a multi-line `TABLE( ... )` declaration
 *  without being fooled by a column type's own parens (e.g. `varchar(50)`). */
function parenDepthDelta(s: string): number {
  let delta = 0;
  for (const ch of s) {
    if (ch === '(') delta++;
    else if (ch === ')') delta--;
  }
  return delta;
}

export function splitTopLevelCommas(str: string): string[] {
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

// ── SP0041 / SP0042 shared scanner: implicit scalar select alias ───────────
//
// Port of ScalarSelectAliaser.cs (source generator) — change one, change all four.
//
// A bare `Select @Return_X;` returns an UNNAMED column, so the runtime shape-key router
// can't match it (SQL Server only — SQLite/PostgreSQL declare scalars as single-column
// temp tables and select a real named column). `findBareScalarSelects` locates every
// qualifying bare single-scalar select (consumed by scalarAliasHints.ts, SP0042);
// `findMultiScalarSelects` locates every select whose top-level column list is 2+
// output-scalar references, which can never be routed regardless of aliasing (SP0041,
// below). Both walk the same underlying scanner (`enumerateScalarSelects`), which skips
// comments (`--`, NESTED `/* */` — T-SQL block comments nest, unlike ANSI SQL), string
// literals, quoted identifiers, and bracketed identifiers exactly like ScalarSelectAliaser.cs.

interface ScalarSelectColumn {
  selectOffset: number;
  variableOffset: number;
  variableLength: number;
  declaredName: string;
  /** true when the entry is EXACTLY a declared output-scalar reference (optionally
   *  followed by an `As` alias) and nothing else. */
  isBareVariable: boolean;
  hasAlias: boolean;
}

/** Port of ScalarSelectAliaser.cs's StatementStarters — change one, change all four. */
const SCALAR_SELECT_STATEMENT_STARTERS = new Set([
  'select', 'insert', 'update', 'delete', 'set', 'declare', 'if', 'while', 'begin', 'end',
  'exec', 'execute', 'return', 'print', 'use', 'with', 'merge', 'truncate', 'drop', 'create',
  'alter', 'go', 'else', 'commit', 'rollback', 'throw', 'raiserror', 'waitfor',
]);

/** Maps a lower-cased `"@return_<name>"` key to its declared base name, for every
 *  declared output-scalar (`role === 'return'`) variable — the scanner's
 *  `scalarsByVariableName`. Shared by SP0041 (below) and SP0042 (scalarAliasHints.ts). */
export function buildScalarsByVariableName(variables: SQuiLVariable[]): Map<string, string> {
  const map = new Map<string, string>();
  for (const v of variables) {
    if (v.role === 'return') {
      map.set(`@return_${v.name}`.toLowerCase(), v.name);
    }
  }
  return map;
}

/** Converts an absolute character offset within `text` into a 0-based (line, character)
 *  position. Offsets returned by this scanner are always relative to whatever text was
 *  handed to it (the full file for SP0041, a body-text slice for SP0042) — the caller
 *  decides what, if any, line offset to add on top. */
export function offsetToPosition(text: string, offset: number): { line: number; character: number } {
  let line = 0;
  let lineStart = 0;
  const end = Math.min(Math.max(offset, 0), text.length);
  for (let i = 0; i < end; i++) {
    if (text[i] === '\n') {
      line++;
      lineStart = i + 1;
    }
  }
  return { line, character: end - lineStart };
}

/**
 * Port of ScalarSelectAliaser.cs's SkipTrivia — change one, change all four.
 * Skips whitespace and comments. T-SQL block comments NEST (unlike ANSI SQL), so a block
 * comment is depth-tracked — an opening delimiter increments depth, a closing one
 * decrements it, and only a closing delimiter at depth 0 actually closes the comment. An
 * unterminated comment simply runs to end-of-text (no infinite loop).
 */
function skipTrivia(text: string, cursor: { i: number }): void {
  while (cursor.i < text.length) {
    const c = text[cursor.i];
    if (/\s/.test(c)) { cursor.i++; continue; }
    if (c === '-' && text[cursor.i + 1] === '-') {
      while (cursor.i < text.length && text[cursor.i] !== '\n') cursor.i++;
      continue;
    }
    if (c === '/' && text[cursor.i + 1] === '*') {
      let depth = 1;
      cursor.i += 2;
      while (cursor.i < text.length && depth > 0) {
        if (text[cursor.i] === '/' && text[cursor.i + 1] === '*') { depth++; cursor.i += 2; }
        else if (text[cursor.i] === '*' && text[cursor.i + 1] === '/') { depth--; cursor.i += 2; }
        else { cursor.i++; }
      }
      continue;
    }
    return;
  }
}

/**
 * Port of ScalarSelectAliaser.cs's SkipNonCode — change one, change all four.
 * Skips one span of non-code at the cursor — whitespace, a comment, a string literal, a
 * quoted identifier, or a bracketed identifier. Returns true when the cursor advanced.
 */
function skipNonCode(text: string, cursor: { i: number }): boolean {
  const before = cursor.i;
  skipTrivia(text, cursor);
  if (cursor.i < text.length && (text[cursor.i] === "'" || text[cursor.i] === '"')) {
    const quote = text[cursor.i];
    cursor.i++;
    while (cursor.i < text.length) {
      if (text[cursor.i] === quote) {
        // Doubled quote is an escape inside the literal.
        if (text[cursor.i + 1] === quote) { cursor.i += 2; continue; }
        cursor.i++;
        break;
      }
      cursor.i++;
    }
  } else if (cursor.i < text.length && text[cursor.i] === '[') {
    while (cursor.i < text.length && text[cursor.i] !== ']') cursor.i++;
    if (cursor.i < text.length) cursor.i++;
  }
  return cursor.i !== before;
}

/** Port of ScalarSelectAliaser.cs's IsWordAt — change one, change all four. */
function isWordAt(text: string, i: number, word: string): boolean {
  if (i < 0 || i + word.length > text.length) return false;
  if (text.slice(i, i + word.length).toLowerCase() !== word.toLowerCase()) return false;
  if (i > 0 && /[A-Za-z0-9_@]/.test(text[i - 1])) return false;
  const after = text[i + word.length];
  if (after !== undefined && /[A-Za-z0-9_]/.test(after)) return false;
  return true;
}

/** Port of ScalarSelectAliaser.cs's PeekWord — change one, change all four. */
function peekWord(text: string, i: number): string {
  if (i >= text.length || !/[A-Za-z_]/.test(text[i])) return '';
  let j = i;
  while (j < text.length && /[A-Za-z0-9_]/.test(text[j])) j++;
  return text.slice(i, j);
}

/** Port of ScalarSelectAliaser.cs's SkipWord — change one, change all four. */
function skipWord(text: string, i: number): number {
  if (i >= text.length) return i + 1;
  if (!/[A-Za-z_@]/.test(text[i])) return i + 1;
  let j = i;
  if (text[j] === '@') j++;
  while (j < text.length && /[A-Za-z0-9_]/.test(text[j])) j++;
  return j > i ? j : i + 1;
}

/**
 * Port of ScalarSelectAliaser.cs's ParseColumnList — change one, change all four.
 * Parses the comma-separated top-level column list that starts at `start`. Returns
 * `columns: null` when the list is not a clean entry sequence (an assignment
 * `Select @X = …`, a `From` clause, a parenthesised expression, …) — `listEnd` is still
 * set on failure so the caller can advance past the failed attempt.
 */
function parseScalarColumnList(
  text: string,
  start: number,
  selectOffset: number,
  scalarsByVariableName: ReadonlyMap<string, string>,
): { columns: ScalarSelectColumn[] | null; listEnd: number } {
  const columns: ScalarSelectColumn[] = [];
  const cursor = { i: start };
  let listEnd = start;

  while (true) {
    skipTrivia(text, cursor);

    let variableOffset = -1;
    let variableLength = 0;
    let declaredName = '';
    let isBareVariable = false;

    // A declared output-scalar reference?
    if (cursor.i < text.length && text[cursor.i] === '@') {
      const nameStart = cursor.i;
      let j = cursor.i + 1;
      while (j < text.length && /[A-Za-z0-9_]/.test(text[j])) j++;
      const variable = text.slice(nameStart, j);
      const declared = scalarsByVariableName.get(variable.toLowerCase());
      if (declared !== undefined) {
        variableOffset = nameStart;
        variableLength = j - nameStart;
        declaredName = declared;
        isBareVariable = true;
        cursor.i = j;
      } else {
        // An @variable that is not a declared output scalar: the statement is not ours.
        listEnd = cursor.i;
        return { columns: null, listEnd };
      }
    } else {
      // Not a scalar reference. Consume one identifier-ish token so a MIXED list is
      // still recognisable as a list (findMultiScalarSelects rejects it via isBareVariable).
      let j = cursor.i;
      while (j < text.length && /[A-Za-z0-9_.]/.test(text[j])) j++;
      if (j === cursor.i) { listEnd = cursor.i; return { columns: null, listEnd }; }   // punctuation/operator
      cursor.i = j;
    }

    skipTrivia(text, cursor);

    // Optional `As <alias>`.
    let hasAlias = false;
    if (isWordAt(text, cursor.i, 'as')) {
      hasAlias = true;
      cursor.i += 2;
      skipTrivia(text, cursor);
      let j = cursor.i;
      if (j < text.length && text[j] === '[') {
        while (j < text.length && text[j] !== ']') j++;
        if (j < text.length) j++;
      } else {
        while (j < text.length && /[A-Za-z0-9_]/.test(text[j])) j++;
      }
      if (j === cursor.i) { listEnd = cursor.i; return { columns: null, listEnd }; }
      cursor.i = j;
      skipTrivia(text, cursor);
    }

    columns.push({ selectOffset, variableOffset, variableLength, declaredName, isBareVariable, hasAlias });

    if (cursor.i < text.length && text[cursor.i] === ',') {
      cursor.i++;
      continue;   // another entry
    }

    // End of the list. It only counts when the next significant token terminates the
    // statement — otherwise the last entry was part of a larger expression.
    listEnd = cursor.i;
    if (cursor.i >= text.length) return { columns, listEnd };                 // end of text
    if (text[cursor.i] === ';') return { columns, listEnd };                  // explicit terminator
    const word = peekWord(text, cursor.i);
    if (word.length > 0 && SCALAR_SELECT_STATEMENT_STARTERS.has(word.toLowerCase())) return { columns, listEnd };
    return { columns: null, listEnd };                                       // `From`, an operator, `(`, `.` …
  }
}

/**
 * Port of ScalarSelectAliaser.cs's EnumerateSelects — change one, change all four.
 * Walks `text` and yields the top-level column list of every `Select` statement whose
 * list consists solely of comma-separated entries. Comments (line and NESTED block),
 * string literals (`'…'`), quoted identifiers (`"…"`), and bracketed identifiers (`[…]`)
 * are skipped so a `Select` inside them is never seen. Any select whose list can't be
 * resolved to a clean entry sequence yields nothing.
 */
function enumerateScalarSelects(
  text: string,
  scalarsByVariableName: ReadonlyMap<string, string>,
): ScalarSelectColumn[][] {
  const results: ScalarSelectColumn[][] = [];
  const cursor = { i: 0 };

  while (cursor.i < text.length) {
    if (skipNonCode(text, cursor)) continue;

    if (!isWordAt(text, cursor.i, 'select')) {
      cursor.i = skipWord(text, cursor.i);
      continue;
    }

    const selectOffset = cursor.i;
    const listStart = cursor.i + 'select'.length;
    const { columns, listEnd } = parseScalarColumnList(text, listStart, selectOffset, scalarsByVariableName);
    if (columns) results.push(columns);

    cursor.i = listEnd > selectOffset ? listEnd : selectOffset + 'select'.length;
  }

  return results;
}

/**
 * Port of ScalarSelectAliaser.cs's FindBareSelects — change one, change all four.
 * Every qualifying bare single-scalar select in `text`, in source order. Consumed by
 * `scalarAliasHints.ts` (SP0042).
 */
export function findBareScalarSelects(
  text: string,
  scalarsByVariableName: ReadonlyMap<string, string>,
): { variableOffset: number; variableLength: number; declaredName: string }[] {
  const results: { variableOffset: number; variableLength: number; declaredName: string }[] = [];
  for (const columns of enumerateScalarSelects(text, scalarsByVariableName)) {
    if (columns.length !== 1) continue;
    const only = columns[0];
    if (only.hasAlias || !only.isBareVariable) continue;
    results.push({
      variableOffset: only.variableOffset,
      variableLength: only.variableLength,
      declaredName: only.declaredName,
    });
  }
  return results;
}

/**
 * Port of ScalarSelectAliaser.cs's FindMultiScalarSelects — change one, change all four.
 * Every select whose top-level column list is 2+ output-scalar references (aliased or
 * not), in source order. A mixed list (a scalar reference plus a real column) is NOT
 * reported here — that is SP0031's domain. Consumed by `lintMultiScalarSelect` (SP0041,
 * below).
 */
export function findMultiScalarSelects(
  text: string,
  scalarsByVariableName: ReadonlyMap<string, string>,
): { selectOffset: number; declaredNames: string[] }[] {
  const results: { selectOffset: number; declaredNames: string[] }[] = [];
  for (const columns of enumerateScalarSelects(text, scalarsByVariableName)) {
    if (columns.length < 2) continue;
    if (!columns.every(c => c.isBareVariable)) continue;
    results.push({ selectOffset: columns[0].selectOffset, declaredNames: columns.map(c => c.declaredName) });
  }
  return results;
}

/**
 * SP0041 (Error) — a Select whose top-level column list is 2+ declared output-scalar
 * references cannot be routed to a response; only one scalar per Select is routable
 * (splitting the select into one-per-scalar is the fix — a REPLACE edit, so this
 * diagnostic deliberately carries no quick-fix). The scan runs over the FULL file text at
 * parse time (`parseSQuiL` populates `result.multiScalarSelects`, above), so these
 * diagnostics are already document-absolute — no body-offset adjustment is needed when
 * wiring this into the diagnostics provider.
 *
 * Port of ScalarSelectAliaser.cs's `FindMultiScalarSelects` — change one, change all four.
 */
export function lintMultiScalarSelect(result: SQuiLParseResult): SQuiLDiagnostic[] {
  return result.multiScalarSelects.map(c => ({
    message:
      `This Select returns more than one output scalar (${c.declaredNames.join(', ')}), which cannot be routed to a response. Use one Select per scalar.`,
    line: c.line,
    startChar: c.character,
    endChar: c.character + c.length,
    severity: 'error' as const,
    code: 'SP0041',
  }));
}

// ── SP0031: unmatched standalone SELECT (editor-only warning) ──────────────
//
// Best-effort, name-focused. Fires when a standalone `Select <col-list> From …`
// in the query body produces a column-name sequence that matches no declared
// @Return_/@Returns_ output signature.  Deliberately ignores `Select *`,
// `Insert Into … Select …`, and any SELECT whose columns can't be statically
// resolved to names (un-aliased expressions → bail, best-effort).
//
// EDITOR-ONLY — must NOT appear in the source generator.

/**
 * SP0031: compare standalone SELECT column names against declared output signatures.
 *
 * Extended (alongside SP0041/SP0042) to cover scalar (`role === 'return'`) outputs too: a
 * scalar select's ALIAS is checked against its declared name — but only when the select
 * carries a resolvable alias. A BARE scalar reference (no alias — SP0042's territory) and
 * the assignment form (`Select @X = …`) never fire here; only a written alias that doesn't
 * match the declared name is a genuine mismatch.
 */
export function lintUnmatchedSelect(parsed: SQuiLParseResult, bodyText: string): SQuiLDiagnostic[] {
  const outputs = parsed.variables.filter(v => (v.role === 'returns' || v.role === 'return-table') && v.columns);
  const scalars = parsed.variables.filter(v => v.role === 'return');
  if (outputs.length === 0 && scalars.length === 0) return [];

  // Set of declared output column-name sequences (lower-cased), for name-based matching.
  // Each declared output scalar's own name is a single-entry key (the scalar extension).
  const declaredNameKeys = new Set(outputs.map(v => v.columns!.map(c => c.name.toLowerCase()).join('|')));
  const scalarsByVariableName = buildScalarsByVariableName(parsed.variables);
  for (const v of scalars) declaredNameKeys.add(v.name.toLowerCase());

  const expectedSignatures = [
    ...outputs.map(v => v.columns!.map(c => c.name).join(', ')),
    ...scalars.map(v => v.name),
  ];

  const diags: SQuiLDiagnostic[] = [];
  const lines = bodyText.split('\n');
  for (let i = 0; i < lines.length; i++) {
    const raw = lines[i];

    // Table case (unchanged): ^\s*select\s+ anchor already excludes Insert Into … Select …
    // and Set … lines. Gated on outputs.length > 0 — a file whose only declared output is a
    // scalar has no table declaredNameKeys entries, so this branch can never match and must
    // not run (previously fired a false-positive SP0031 on every multi-column SELECT).
    const mFrom = outputs.length > 0 ? /^\s*select\s+(?!\*)(.+?)\s+from\s/i.exec(raw) : null;
    if (mFrom) {
      const cols = extractSelectColumnNames(mFrom[1]);
      if (!cols) continue;                                // not statically inferable -> skip (best-effort)
      const key = cols.map(c => c.toLowerCase()).join('|');
      if (declaredNameKeys.has(key)) continue;
      diags.push({
        message: `This SELECT's columns (${cols.join(', ')}) match no declared @Returns_/@Return_ output signature. Expected one of: ${expectedSignatures.join(' | ')}. Add AS aliases (and CAST base types) to match, or use Insert Into @Returns_X … ; Select * From @Returns_X;.`,
        line: i, startChar: 0, endChar: raw.length,
        severity: 'warning', code: 'SP0031',
      });
      continue;
    }

    // Scalar case (the extension): the FROM requirement is relaxed to also consider a
    // bare `Select <expr>[;]` line, but only a RESOLVABLE alias that mismatches the
    // declared name fires. A bare reference (no alias) and the assignment form are left
    // alone — they are not resolvable mismatches, just different shapes entirely.
    if (scalars.length === 0) continue;
    const mBare = /^\s*select\s+(?!\*)(.+?)\s*;?\s*$/i.exec(raw);
    if (!mBare) continue;
    const scalarMatch = /^\s*(@[A-Za-z_][A-Za-z0-9_]*)\s*(.*)$/.exec(mBare[1]);
    if (!scalarMatch) continue;
    const declaredName = scalarsByVariableName.get(scalarMatch[1].toLowerCase());
    if (declaredName === undefined) continue;             // not a declared output scalar reference
    const rest = scalarMatch[2].trim();
    if (rest === '') continue;                            // bare — SP0042's territory
    if (/^=/.test(rest)) continue;                        // assignment form

    const aliasMatch = /^as\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$/i.exec(rest);
    if (!aliasMatch) continue;                            // not a resolvable alias -> bail (best-effort)
    const alias = aliasMatch[1];
    if (alias.toLowerCase() === declaredName.toLowerCase()) continue;   // correctly aliased

    diags.push({
      message: `This SELECT's columns (${alias}) match no declared @Returns_/@Return_ output signature. Expected one of: ${expectedSignatures.join(' | ')}. Add AS aliases (and CAST base types) to match, or use Insert Into @Returns_X … ; Select * From @Returns_X;.`,
      line: i, startChar: 0, endChar: raw.length,
      severity: 'warning', code: 'SP0031',
    });
  }
  return diags;
}

/** Best-effort: returns output column names for a simple comma list, or null if not inferable. */
function extractSelectColumnNames(list: string): string[] | null {
  const parts = splitTopLevelCommas(list);
  const names: string[] = [];
  for (const p of parts) {
    const asMatch = /\s+as\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$/i.exec(p);
    if (asMatch) { names.push(asMatch[1]); continue; }
    const bare = /^\s*\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$/.exec(p);           // bare column
    const dotted = /\.\s*\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*$/.exec(p);        // table.column
    if (dotted) { names.push(dotted[1]); continue; }
    if (bare) { names.push(bare[1]); continue; }
    return null;   // an un-aliased expression -> can't infer a column name -> bail (best-effort)
  }
  return names;
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
