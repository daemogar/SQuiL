/**
 * SQuiL C# Preview Generator
 *
 * Produces a human-readable approximation of what the SQuiL source generator
 * would emit for a given .squil file.  This is intentionally a preview — it
 * does not attempt to replicate every nuance of the real generator.
 */

import { SQuiLParseResult, SQuiLVariable } from './parser';

// ─── SQL → C# type mapping ────────────────────────────────────────────────

const SQL_TO_CS: Record<string, string> = {
  bigint: 'long',
  binary: 'byte[]',
  bit: 'bool',
  char: 'string',
  date: 'DateOnly',
  datetime: 'DateTime',
  datetime2: 'DateTime',
  datetimeoffset: 'DateTimeOffset',
  decimal: 'decimal',
  float: 'double',
  image: 'byte[]',
  int: 'int',
  money: 'decimal',
  nchar: 'string',
  ntext: 'string',
  numeric: 'decimal',
  nvarchar: 'string',
  real: 'float',
  smalldatetime: 'DateTime',
  smallint: 'short',
  smallmoney: 'decimal',
  text: 'string',
  time: 'TimeOnly',
  timestamp: 'byte[]',
  tinyint: 'byte',
  uniqueidentifier: 'Guid',
  varbinary: 'byte[]',
  varchar: 'string',
  xml: 'string',
};

export function sqlToCSharp(sqlType: string): string {
  const base = sqlType.toLowerCase().replace(/\s*\(.*\)/, '').trim();
  return SQL_TO_CS[base] ?? 'object';
}

/**
 * Record-type name SQuiL generates for a table/object variable.
 * The Table/Object suffix was dropped in TODO #3 — the bare name is used directly.
 */
function recordTypeName(v: SQuiLVariable): string {
  return v.name;
}

function isCollectionRole(v: SQuiLVariable): boolean {
  return v.role === 'params' || v.role === 'returns';
}

// ─── Nested-objects key graph (preview-only mirror of SQuiLKeyGraph.cs) ───

/**
 * Minimal preview mirror of the generator's `SQuiLKeyGraph`
 * (`SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs`): a table/object
 * OUTPUT variable's key is its single `Primary Key` column; any OTHER output
 * variable carrying a column of that exact name becomes its child. Variables
 * nobody links to are roots. Only OUTPUT (`@Return*`) table/object variables
 * participate — input nesting is out of scope (matches the generator, which
 * builds its graph from OUTPUT blocks only).
 *
 * Simplified relative to the generator: ambiguous (>1 distinct parent) or
 * cyclic links are build-time errors owned by the generator/editor
 * diagnostics, not the preview — here the first matching PK owner silently
 * wins so the preview always renders *something* reasonable (graceful
 * degradation to the flat shape when there are no links at all).
 */
interface NestedGraph {
  /** Variables that are not any other variable's child — the top-level Response members. */
  roots: SQuiLVariable[];
  /** parent → its direct children, in declaration order. */
  childrenOf: Map<SQuiLVariable, SQuiLVariable[]>;
  /** true when `v` collapses into a parent record instead of staying top-level. */
  isChild: (v: SQuiLVariable) => boolean;
}

function buildNestedGraph(tableVars: SQuiLVariable[]): NestedGraph {
  // key column name (lower-cased) -> the variable whose Primary Key it is.
  const pkOwner = new Map<string, SQuiLVariable>();
  for (const v of tableVars) {
    const pk = (v.columns ?? []).find(c => c.isPrimaryKey);
    if (pk && !pkOwner.has(pk.name.toLowerCase())) {
      pkOwner.set(pk.name.toLowerCase(), v);
    }
  }

  const parentOf = new Map<SQuiLVariable, SQuiLVariable>();
  for (const child of tableVars) {
    for (const col of child.columns ?? []) {
      const owner = pkOwner.get(col.name.toLowerCase());
      if (owner && owner !== child) {
        parentOf.set(child, owner);
        break;
      }
    }
  }

  const childrenOf = new Map<SQuiLVariable, SQuiLVariable[]>();
  for (const v of tableVars) {
    const parent = parentOf.get(v);
    if (!parent) continue;
    const list = childrenOf.get(parent);
    if (list) list.push(v);
    else childrenOf.set(parent, [v]);
  }

  const roots = tableVars.filter(v => !parentOf.has(v));
  return { roots, childrenOf, isChild: v => parentOf.has(v) };
}

function getPropertyType(v: SQuiLVariable, modelsNs?: string): string {
  if (v.role === 'params' || v.role === 'returns') {
    const typeName = modelsNs ? `${modelsNs}.${recordTypeName(v)}` : recordTypeName(v);
    return `List<${typeName}>?`;
  }
  if (v.role === 'param-table' || v.role === 'return-table') {
    const typeName = modelsNs ? `${modelsNs}.${recordTypeName(v)}` : recordTypeName(v);
    return `${typeName}?`;
  }
  // Scalars: non-nullable unless an explicit `null` marker was declared. Ref types are NOT auto-?.
  const cs = sqlToCSharp(v.sqlType);
  return v.nullable ? `${cs}?` : cs;
}

// ─── Main entry point ─────────────────────────────────────────────────────

export function generateCSharpPreview(
  parsed: SQuiLParseResult,
  queryName: string,
  namespace = 'YourNamespace',
  enabled = false,
  debugRollback = true,
): string {
  const db = parsed.database ?? '/* database */';
  const lines: string[] = [];

  const params = parsed.variables.filter(v =>
    v.role === 'param' || v.role === 'params' || v.role === 'param-table',
  );
  const returns = parsed.variables.filter(v =>
    v.role === 'return' || v.role === 'returns' || v.role === 'return-table',
  );

  // Collect all table-valued variables that need row records
  const paramTableVars = params.filter(v => v.columns && v.columns.length > 0);
  const returnTableVars = returns.filter(v => v.columns && v.columns.length > 0);
  const tableVars = [...paramTableVars, ...returnTableVars];
  // The Namespace override on [SQuiLQuery] is generator-only; editors cannot read C# attributes,
  // so the preview always uses the default "Models" sub-namespace segment.
  const modelsNs = `${namespace}.Models`;

  // Nested-objects: only OUTPUT table/object variables link into a parent/child
  // graph (input nesting is out of scope, matching the generator). Children
  // collapse into their parent record and drop off the Response top level.
  const graph = buildNestedGraph(returnTableVars);
  const responseVars = returns.filter(v => !graph.isChild(v));

  banner(lines, queryName, db);
  lines.push('');
  lines.push(`namespace ${namespace};`);
  lines.push('');

  // ── Enum entry hint
  lines.push(`// ── QueryFiles enum entry ────────────────────────────────`);
  lines.push(`// Generated by SQuiL.SourceGenerator — do not edit manually.`);
  lines.push(`// Your QueryFiles enum will include:`);
  lines.push(`//   public enum QueryFiles { ..., ${queryName} }`);
  lines.push('');

  // ── using for the Models sub-namespace (only when row records exist)
  if (tableVars.length > 0) {
    lines.push(`using ${modelsNs};`);
    lines.push('');
  }

  // ── Request record (always partial; specials are opt-in)
  lines.push(`// ── Request ─────────────────────────────────────────────`);
  emitModelRecord(lines, `${queryName}Request`, params, /*isResponse*/ false, parsed.variables, modelsNs);

  // ── Response record (only nesting ROOTS appear at the top level — a
  // child collapses into its parent record as a member instead)
  if (returns.length > 0) {
    lines.push(`// ── Response ────────────────────────────────────────────`);
    emitModelRecord(lines, `${queryName}Response`, responseVars, /*isResponse*/ true, undefined, modelsNs);
  }

  // ── Data context
  lines.push(`// ── DataContext ─────────────────────────────────────────`);
  lines.push(`// SQuiL emits this method into your partial class. You may omit the`);
  lines.push(`// base type and constructor — SQuiL supplies both when absent:`);
  lines.push(`//   public partial class ${queryName}DataContext { }`);
  lines.push(`// (Add your own constructor to customize; it must call : base(configuration).)`);
  lines.push('');

  const responseType = returns.length === 0
    ? 'SQuiLResultType'
    : `SQuiLResultType<${queryName}Response>`;

  lines.push(`public async Task<${responseType}> Process${queryName}Async(`);
  lines.push(`    ${queryName}Request request,`);
  lines.push(`    CancellationToken cancellationToken = default!)`);
  lines.push(`{`);
  if (enabled) {
    // Detect @Debug declaration to determine the correct commit gate.
    const hasDebug = parsed.variables.some(v => v.role === 'debug');
    const commitGate = (hasDebug && debugRollback)
      ? 'errors.Count == 0 && !__debug'
      : 'errors.Count == 0';

    lines.push(`    await connection.OpenAsync(cancellationToken);`);
    lines.push(``);
    lines.push(`    using var transaction = connection.BeginTransaction();`);
    lines.push(`    command.Transaction = transaction;`);
    lines.push(``);
    lines.push(`    /* …read / execute… */`);
    lines.push(``);
    lines.push(`    if (${commitGate})`);
    lines.push(`        transaction.Commit();`);
    lines.push(`    else`);
    lines.push(`        transaction.Rollback();`);
  } else {
    lines.push(`    /* generated body */`);
  }
  lines.push(`}`);
  lines.push('');

  // ── DI extension hint
  lines.push(`// ── Dependency Injection ────────────────────────────────`);
  lines.push(`// SQuiL emits an AddSQuiL extension that registers every data context:`);
  lines.push(`//`);
  lines.push(`//   builder.AddSQuiL();`);
  lines.push(`//`);
  lines.push(`// Connection string key: "ConnectionStrings:${db}"`);

  // ── Row records emitted into the .Models sub-namespace
  if (tableVars.length > 0) {
    lines.push('');
    lines.push(`// ── Row records ─────────────────────────────────────────`);
    lines.push(`// Row records live in the .Models sub-namespace, mirroring the generator.`);
    lines.push(`namespace ${modelsNs};`);
    lines.push('');
    for (const v of tableVars) {
      emitTableRecord(lines, recordTypeName(v), v, modelsNs, graph.childrenOf.get(v));
    }
  }

  return lines.join('\n');
}

// ─── Helpers ─────────────────────────────────────────────────────────────

function banner(lines: string[], queryName: string, db: string): void {
  lines.push(`// ╔═══════════════════════════════════════════════════════╗`);
  lines.push(`// ║  SQuiL Generated C# Preview                          ║`);
  lines.push(`// ╠═══════════════════════════════════════════════════════╣`);
  lines.push(`// ║  Query    : ${pad(queryName, 41)}║`);
  lines.push(`// ║  Database : ${pad(db, 41)}║`);
  lines.push(`// ╠═══════════════════════════════════════════════════════╣`);
  lines.push(`// ║  ⚠  This is a PREVIEW only.                          ║`);
  lines.push(`// ║     Actual code is emitted by SQuiL.SourceGenerator  ║`);
  lines.push(`// ║     when you run  dotnet build.                       ║`);
  lines.push(`// ╚═══════════════════════════════════════════════════════╝`);
}

function pad(s: string, len: number): string {
  return s.length >= len ? s.substring(0, len) : s + ' '.repeat(len - s.length);
}

/**
 * Approximates the generator's per-type default initializer for a column
 * `DEFAULT <raw>`: decimal gets an `m` suffix, single-quoted SQL strings become
 * double-quoted C#, everything else is emitted as-is (date/guid are approximate
 * in the preview — the real generator wraps them in a Parse call).
 */
function csharpDefault(sqlType: string, raw: string): string {
  if (raw.startsWith("'") && raw.endsWith("'")) return `"${raw.slice(1, -1)}"`;
  const base = sqlType.toLowerCase().replace(/\s*\(.*\)/, '').trim();
  if (base === 'decimal' || base === 'numeric' || base === 'money' || base === 'smallmoney') return `${raw}m`;
  return raw;
}

function emitTableRecord(
  lines: string[],
  typeName: string,
  v: SQuiLVariable,
  modelsNs?: string,
  children?: SQuiLVariable[],
): void {
  if (!v.columns || v.columns.length === 0) return;

  const csType = (col: typeof v.columns[number]): string => {
    const cs = sqlToCSharp(col.sqlType);
    return col.nullable ? `${cs}?` : cs;
  };

  const positional = v.columns.filter(c => !c.defaultValue);
  const defaulted = v.columns.filter(c => c.defaultValue);
  const params = positional.map(c => `${csType(c)} ${c.name}`).join(', ');
  const hasChildren = children !== undefined && children.length > 0;

  if (defaulted.length === 0 && !hasChildren) {
    lines.push(`public partial record ${typeName}(${params});`);
    lines.push('');
    return;
  }

  lines.push(`public partial record ${typeName}(${params})`);
  lines.push(`{`);
  defaulted.forEach(col => {
    lines.push(`    public ${csType(col)} ${col.name} { get; init; } = ${csharpDefault(col.sqlType, col.defaultValue!)};`);
  });
  // Nested-objects: a child table/object collapses into its parent record as a
  // plain settable member (no `= []`/`= default!` initializer — those belong
  // only to top-level Response properties), typed the same as a top-level
  // list/object member (List<ns.Models.Child>? for a list child, ns.Models.Child?
  // for an object child).
  if (hasChildren) {
    children!.forEach(child => {
      lines.push(`    public ${getPropertyType(child, modelsNs)} ${child.name} { get; set; }`);
    });
  }
  lines.push(`}`);
  lines.push('');
}

function emitModelRecord(
  lines: string[],
  typeName: string,
  vars: SQuiLVariable[],
  isResponse: boolean,
  allVars?: SQuiLVariable[],
  modelsNs?: string,
): void {
  lines.push(`public partial record ${typeName}`);
  lines.push(`{`);

  // *Request specials are OPT-IN — each appears only when its bare special is
  // declared in the SQL header. `@Debug` → `bool Debug`, `@SuppressDebug` →
  // `bool SuppressDebug` (replaces the old always-on `DebugOnly`), `@AsOfDate`
  // → a nullable typed property. `@EnvironmentName` is a sent parameter only,
  // never a property.
  if (!isResponse) {
    const declared = allVars ?? [];
    const hasDebug = declared.some(v => v.role === 'debug');
    const hasSuppressDebug = declared.some(v => v.role === 'suppressDebug');
    const asOfDate = declared.find(v => v.role === 'asOfDate');

    if (hasDebug) lines.push(`    public bool Debug { get; set; }`);
    if (hasSuppressDebug) lines.push(`    public bool SuppressDebug { get; set; }`);
    if (asOfDate) {
      // Take only the type token (drop any "= default" the SQL initializer adds),
      // matching the generator which maps the bare declared type. AsOfDate is
      // always nullable on *Request.
      const asOfType = asOfDate.sqlType.split(/[\s=]/)[0];
      lines.push(`    public ${sqlToCSharp(asOfType)}? AsOfDate { get; set; }`);
    }

    if ((hasDebug || hasSuppressDebug || asOfDate) && vars.length > 0) lines.push('');
  }

  vars.forEach(v => {
    const initializer = (!isResponse && isCollectionRole(v)) ? ' = []' : '';
    lines.push(`    public ${getPropertyType(v, modelsNs)} ${v.name} { get; set; }${initializer};`);
  });

  lines.push(`}`);
  lines.push('');
}
