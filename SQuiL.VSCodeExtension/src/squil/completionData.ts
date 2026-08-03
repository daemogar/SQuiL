/**
 * SQuiL completion data — dialect-gated snippet & type tables.
 *
 * Pure module (no `vscode` dependency — see `providers/completionProvider.ts`
 * for the thin VS Code wrapper that maps these descriptors to CompletionItems).
 * Split out of the provider so the dialect-gated selection is unit-testable
 * with `node:test`, mirroring the `src/squil/*` pattern.
 *
 * Port: SQuiL.SsmsExtension/Completion/SQuiLCompletionData.cs and
 *       SQuiL.VisualStudioExtension/Completion/SQuiLCompletionData.cs —
 *       change one side, change all.
 */

import { EditorDialect } from './dialect';

// ─── SQL type vocabularies ─────────────────────────────────────────────────

export const SQL_TYPES = [
  'bigint', 'binary', 'bit', 'char', 'date',
  'datetime', 'datetime2', 'datetimeoffset',
  'decimal', 'float', 'image', 'int', 'money',
  'nchar', 'ntext', 'numeric', 'nvarchar',
  'real', 'smalldatetime', 'smallint', 'smallmoney',
  'text', 'time', 'tinyint', 'uniqueidentifier',
  'varbinary', 'varchar', 'xml',
  // Common parameterised variants
  'varchar(50)', 'varchar(100)', 'varchar(255)', 'varchar(max)',
  'nvarchar(50)', 'nvarchar(100)', 'nvarchar(255)', 'nvarchar(max)',
  'decimal(18, 2)', 'decimal(18, 4)',
  'char(1)', 'char(10)',
];

/** SQLite's type vocabulary — offered instead of SQL_TYPES when the owning .csproj resolves to the SQLite dialect. */
export const SQLITE_TYPES = [
  'integer', 'text', 'real', 'blob', 'numeric', 'decimal',
  'boolean', 'date', 'datetime', 'guid', 'uniqueidentifier',
];

/** PostgreSQL's type vocabulary — offered instead of SQL_TYPES when the owning .csproj resolves to the Postgres dialect. */
export const POSTGRES_TYPES = [
  'int4', 'int8', 'int2', 'integer', 'bigint', 'smallint',
  'text', 'varchar', 'char', 'bpchar', 'bytea', 'uuid',
  'bool', 'boolean', 'timestamp', 'timestamptz', 'date', 'time',
  'numeric', 'decimal', 'real', 'double precision', 'money', 'json', 'jsonb',
];

/** Selects SQL_TYPES, SQLITE_TYPES, or POSTGRES_TYPES for the given dialect. */
export function typesFor(dialect: EditorDialect): string[] {
  if (dialect === 'sqlite') return SQLITE_TYPES;
  if (dialect === 'postgres') return POSTGRES_TYPES;
  return SQL_TYPES;
}

/**
 * Temp-table-family column-type-position trigger.
 *
 * A temp-table-header dialect (SQLite, PostgreSQL) declaration is
 * `Create Temp Table <name> ( <col> <type>, … )` — there is no T-SQL
 * `Declare @x <type>` / `AS <type>` position, so the existing type-position
 * triggers never fire where an author actually types a column type. This
 * matches the text-before-caret when the caret sits right after a column
 * NAME (plus whitespace) inside the still-open `(` of a `Create Temp Table`
 * statement — either the first column (right after the `(`) or a later column
 * (right after a `,`). It deliberately does NOT match once a type has already
 * been typed, after the paren closes, or at `AS ` / `Declare @x ` positions, so
 * temp-table types are offered only where a column type belongs. Dialect-
 * agnostic by construction (no `@` sigil in either dialect's header), so the
 * same regex serves SQLite and PostgreSQL alike — callers gate on the
 * resolved dialect before calling this.
 *
 * Port: `SqliteColumnTypePosition` in SQuiLCompletionSource.cs (SSMS + Visual
 * Studio) — same pattern, change one side, change all.
 */
const SQLITE_COLUMN_TYPE_POSITION =
  /Create\s+Temp\s+Table\s+\w+\s*\((?:\s*|[^)]*,\s*)\w+\s+$/i;

/** True when `textBefore` (the current line up to the caret) is a temp-table (SQLite/Postgres) column-type position. */
export function isSqliteColumnTypePosition(textBefore: string): boolean {
  return SQLITE_COLUMN_TYPE_POSITION.test(textBefore);
}

// ─── SQuiL variable descriptors ────────────────────────────────────────────

export interface VarDescriptor {
  prefix: string;
  snippet: string;
  detail: string;
  docs: string;
}

/** T-SQL header declarations: `Declare @Prefix_Name …`. */
export const HEADER_VARS: VarDescriptor[] = [
  {
    prefix: '@Param_',
    snippet: '@Param_${1:Name} ${2:varchar(100)}',
    detail: 'Input scalar parameter',
    docs:
      'Maps to a property on the generated `*Request` record.\n\n' +
      '```sql\nDeclare @Param_UserID int;\n```',
  },
  {
    prefix: '@Params_',
    snippet: '@Params_${1:Items} table (${2:ID int})',
    detail: 'Input table-valued parameter → IEnumerable<T>',
    docs:
      'Maps to an `IEnumerable<ItemT>` property on `*Request`.\n\n' +
      '```sql\nDeclare @Params_UserIDs table (ID int);\n```',
  },
  {
    prefix: '@Return_',
    snippet: '@Return_${1:Name} ${2:int}',
    detail: 'Output scalar variable',
    docs:
      'Maps to a property on the generated `*Response` record.\n\n' +
      '```sql\nDeclare @Return_Count int;\n```',
  },
  {
    prefix: '@Returns_',
    snippet: '@Returns_${1:Items} table (${2:ID int, Name varchar(100)})',
    detail: 'Output table variable → IEnumerable<T>',
    docs:
      'Maps to an `IEnumerable<ItemT>` property on `*Response`.\n\n' +
      '```sql\nDeclare @Returns_Users table (ID int, Name varchar(100));\n```',
  },
  {
    prefix: '@Debug',
    snippet: '@Debug bit = 1',
    detail: 'Debug flag — on *Request as `bool Debug` when declared',
    docs:
      'Opt-in special SQuiL variable. `*Request` exposes `bool Debug` **only when `@Debug` is declared**. ' +
      'Declare `@SuppressDebug` alongside it to gate the auto-debug expression. ' +
      'The default `= 1` is convenient when running the query directly in SSMS.\n\n' +
      '```sql\nDeclare @Debug bit = 1;\n```',
  },
  {
    prefix: '@SuppressDebug',
    snippet: '@SuppressDebug bit = 0',
    detail: 'Suppress auto-debug — on *Request as `bool SuppressDebug` when declared',
    docs:
      'Opt-in special SQuiL variable. Gates the auto-debug expression (replaces the old `DebugOnly` property). ' +
      'Must be declared together with `@Debug`, otherwise **SP0019** is reported.\n\n' +
      '```sql\nDeclare @Debug bit = 1;\nDeclare @SuppressDebug bit = 0;\n```',
  },
  {
    prefix: '@EnvironmentName',
    snippet: '@EnvironmentName varchar(50)',
    detail: 'Environment name — resolved by SQuiLBaseDataContext',
    docs:
      'Resolved by `SQuiLBaseDataContext` from `IConfiguration["EnvironmentName"]` or the ' +
      '`ASPNETCORE_ENVIRONMENT` environment variable (defaulting to `"Development"`). ' +
      'Declare in SQL only when the query body needs to read it. Sent as a parameter only — never a C# property.\n\n' +
      '```sql\nDeclare @EnvironmentName varchar(50);\n```',
  },
  {
    prefix: '@AsOfDate',
    snippet: "@AsOfDate date = '2008-10-01'",
    detail: 'Point-in-time — nullable typed property on *Request',
    docs:
      'Opt-in special SQuiL variable. Caller-supplied point-in-time value, surfaced as a **nullable typed property** ' +
      'on `*Request` (its type follows the SQL type map, e.g. `date` → `DateOnly?`). ' +
      'When null, the **current time at execution** is substituted; the SQL initializer is ignored at runtime.\n\n' +
      "```sql\nDeclare @AsOfDate date = '2008-10-01';\n```",
  },
];

/**
 * SQLite header declarations: `Create Temp Table <Prefix>_<Name> (...)`.
 * SQLite has no `@` sigil and no `Declare`/`Use` — direction (Param/Return)
 * and cardinality (singular/plural) are carried entirely by the bare table
 * name, exactly as the generator's SQLite header parser expects. A singular
 * single-column declaration collapses to a scalar; anything wider is an object.
 */
export const SQLITE_HEADER_VARS: VarDescriptor[] = [
  {
    prefix: 'Param_',
    snippet: 'Create Temp Table Param_${1:Name} (${2:Value TEXT})',
    detail: 'Input scalar/object — property on *Request',
    docs:
      'A single-column `Param_` collapses to an input **scalar**; a wider one is an input **object**. ' +
      'Maps to a property on the generated `*Request` record.\n\n' +
      '```sql\nCreate Temp Table Param_Age (Age INTEGER);\n```',
  },
  {
    prefix: 'Params_',
    snippet: 'Create Temp Table Params_${1:Items} (${2:ID INTEGER})',
    detail: 'Input list → IEnumerable<T> on *Request',
    docs:
      'Maps to an `IEnumerable<ItemT>` property on `*Request`.\n\n' +
      '```sql\nCreate Temp Table Params_Roster (PersonID INTEGER Primary Key, Name TEXT);\n```',
  },
  {
    prefix: 'Return_',
    snippet: 'Create Temp Table Return_${1:Name} (${2:Value INTEGER})',
    detail: 'Output scalar/object — property on *Response',
    docs:
      'A single-column `Return_` collapses to an output **scalar**; a wider one is an output **object**. ' +
      'Maps to a property on the generated `*Response` record.\n\n' +
      '```sql\nCreate Temp Table Return_Total (Total INTEGER);\n```',
  },
  {
    prefix: 'Returns_',
    snippet: 'Create Temp Table Returns_${1:Items} (${2:ID INTEGER, Name TEXT})',
    detail: 'Output list → IEnumerable<T> on *Response',
    docs:
      'Maps to an `IEnumerable<ItemT>` property on `*Response`.\n\n' +
      '```sql\nCreate Temp Table Returns_Echoed (PersonID INTEGER Primary Key, Name TEXT);\n```',
  },
];

/**
 * PostgreSQL header declarations: `Create Temp Table <Prefix>_<Name> (...)`.
 * Same temp-table-header shape as SQLite (no `@` sigil, no `Declare`/`Use`,
 * direction/cardinality carried by the bare table name) — only the column
 * type spellings differ (PostgreSQL types instead of SQLite's).
 */
export const POSTGRES_HEADER_VARS: VarDescriptor[] = [
  {
    prefix: 'Param_',
    snippet: 'Create Temp Table Param_${1:Name} (${2:Value text})',
    detail: 'Input scalar/object — property on *Request',
    docs:
      'A single-column `Param_` collapses to an input **scalar**; a wider one is an input **object**. ' +
      'Maps to a property on the generated `*Request` record.\n\n' +
      '```sql\nCreate Temp Table Param_Age (Age integer);\n```',
  },
  {
    prefix: 'Params_',
    snippet: 'Create Temp Table Params_${1:Items} (${2:ID integer})',
    detail: 'Input list → IEnumerable<T> on *Request',
    docs:
      'Maps to an `IEnumerable<ItemT>` property on `*Request`.\n\n' +
      '```sql\nCreate Temp Table Params_Roster (PersonID integer Primary Key, Name text);\n```',
  },
  {
    prefix: 'Return_',
    snippet: 'Create Temp Table Return_${1:Name} (${2:Value integer})',
    detail: 'Output scalar/object — property on *Response',
    docs:
      'A single-column `Return_` collapses to an output **scalar**; a wider one is an output **object**. ' +
      'Maps to a property on the generated `*Response` record.\n\n' +
      '```sql\nCreate Temp Table Return_Total (Total integer);\n```',
  },
  {
    prefix: 'Returns_',
    snippet: 'Create Temp Table Returns_${1:Items} (${2:ID integer, Name text})',
    detail: 'Output list → IEnumerable<T> on *Response',
    docs:
      'Maps to an `IEnumerable<ItemT>` property on `*Response`.\n\n' +
      '```sql\nCreate Temp Table Returns_Echoed (PersonID integer Primary Key, Name text);\n```',
  },
];

/** Selects HEADER_VARS, SQLITE_HEADER_VARS, or POSTGRES_HEADER_VARS for the given dialect. */
export function headerVarsFor(dialect: EditorDialect): VarDescriptor[] {
  if (dialect === 'sqlite') return SQLITE_HEADER_VARS;
  if (dialect === 'postgres') return POSTGRES_HEADER_VARS;
  return HEADER_VARS;
}

// ─── File-level scaffold snippets ──────────────────────────────────────────

export interface FileSnippetDescriptor {
  label: string;
  snippet: string;
  detail: string;
}

/** T-SQL file scaffolds. */
export const FILE_SNIPPETS: FileSnippetDescriptor[] = [
  {
    label: 'squil-file',
    snippet: [
      '--Name: ${1:QueryName}',
      '',
      'Declare @Param_${2:Name} ${3:varchar(100)};',
      'Declare @Return_${4:Result} ${5:int};',
      '',
      'Use [${6:DatabaseName}];',
      '',
      '-- SQL body',
      'Set @Return_${4:Result} = (Select ${7:Count(*)} From ${8:TableName} Where ${9:1=1});',
      'Select @Return_${4:Result};',
    ].join('\n'),
    detail: 'Scaffold a complete SQuiL file',
  },
  {
    label: 'squil-declare-input',
    snippet: 'Declare @Param_${1:Name} ${2:varchar(100)};',
    detail: 'Declare input scalar parameter',
  },
  {
    label: 'squil-declare-input-table',
    snippet: ['Declare @Params_${1:Items} table (', '    ${2:ID} ${3:int}', ');'].join('\n'),
    detail: 'Declare input table-valued parameter',
  },
  {
    label: 'squil-declare-output',
    snippet: 'Declare @Return_${1:Name} ${2:int};',
    detail: 'Declare output scalar variable',
  },
  {
    label: 'squil-declare-output-table',
    snippet: [
      'Declare @Returns_${1:Items} table (',
      '    ${2:ID} ${3:int},',
      '    ${4:Name} ${5:varchar(100)}',
      ');',
    ].join('\n'),
    detail: 'Declare output table variable',
  },
];

/**
 * SQLite file scaffolds — `Create Temp Table` declarations, NO `Use` line
 * (SQLite has no USE statement). Mirrors FILE_SNIPPETS one-for-one.
 */
export const SQLITE_FILE_SNIPPETS: FileSnippetDescriptor[] = [
  {
    label: 'squil-file',
    snippet: [
      '--Name: ${1:QueryName}',
      '',
      'Create Temp Table Params_${2:Roster} (${3:PersonID INTEGER Primary Key, Name TEXT});',
      'Create Temp Table Returns_${4:Echoed} (${5:PersonID INTEGER Primary Key, Name TEXT});',
      '',
      '-- SQL body',
      'Insert Into Returns_${4:Echoed} (${6:PersonID, Name}) Select ${7:PersonID, Name} From Params_${2:Roster};',
      'Select ${8:PersonID, Name} From Returns_${4:Echoed};',
    ].join('\n'),
    detail: 'Scaffold a complete SQLite SQuiL file (Create Temp Table, no Use)',
  },
  {
    label: 'squil-declare-input',
    snippet: 'Create Temp Table Param_${1:Name} (${2:Value TEXT});',
    detail: 'Declare input scalar/object (Create Temp Table)',
  },
  {
    label: 'squil-declare-input-table',
    snippet: ['Create Temp Table Params_${1:Items} (', '    ${2:ID INTEGER}', ');'].join('\n'),
    detail: 'Declare input list (Create Temp Table)',
  },
  {
    label: 'squil-declare-output',
    snippet: 'Create Temp Table Return_${1:Name} (${2:Value INTEGER});',
    detail: 'Declare output scalar/object (Create Temp Table)',
  },
  {
    label: 'squil-declare-output-table',
    snippet: [
      'Create Temp Table Returns_${1:Items} (',
      '    ${2:ID INTEGER},',
      '    ${3:Name TEXT}',
      ');',
    ].join('\n'),
    detail: 'Declare output list (Create Temp Table)',
  },
];

/**
 * PostgreSQL file scaffolds — `Create Temp Table` declarations, NO `Use` line
 * (PostgreSQL has no USE statement; the database is fixed by the connection
 * string). Mirrors SQLITE_FILE_SNIPPETS one-for-one, with PostgreSQL types.
 */
export const POSTGRES_FILE_SNIPPETS: FileSnippetDescriptor[] = [
  {
    label: 'squil-file',
    snippet: [
      '--Name: ${1:QueryName}',
      '',
      'Create Temp Table Params_${2:Roster} (${3:PersonID integer Primary Key, Name text});',
      'Create Temp Table Returns_${4:Echoed} (${5:PersonID integer Primary Key, Name text});',
      '',
      '-- SQL body',
      'Insert Into Returns_${4:Echoed} (${6:PersonID, Name}) Select ${7:PersonID, Name} From Params_${2:Roster};',
      'Select ${8:PersonID, Name} From Returns_${4:Echoed};',
    ].join('\n'),
    detail: 'Scaffold a complete PostgreSQL SQuiL file (Create Temp Table, no Use)',
  },
  {
    label: 'squil-declare-input',
    snippet: 'Create Temp Table Param_${1:Name} (${2:Value text});',
    detail: 'Declare input scalar/object (Create Temp Table)',
  },
  {
    label: 'squil-declare-input-table',
    snippet: ['Create Temp Table Params_${1:Items} (', '    ${2:ID integer}', ');'].join('\n'),
    detail: 'Declare input list (Create Temp Table)',
  },
  {
    label: 'squil-declare-output',
    snippet: 'Create Temp Table Return_${1:Name} (${2:Value integer});',
    detail: 'Declare output scalar/object (Create Temp Table)',
  },
  {
    label: 'squil-declare-output-table',
    snippet: [
      'Create Temp Table Returns_${1:Items} (',
      '    ${2:ID integer},',
      '    ${3:Name text}',
      ');',
    ].join('\n'),
    detail: 'Declare output list (Create Temp Table)',
  },
];

/** Selects FILE_SNIPPETS, SQLITE_FILE_SNIPPETS, or POSTGRES_FILE_SNIPPETS for the given dialect. */
export function fileSnippetsFor(dialect: EditorDialect): FileSnippetDescriptor[] {
  if (dialect === 'sqlite') return SQLITE_FILE_SNIPPETS;
  if (dialect === 'postgres') return POSTGRES_FILE_SNIPPETS;
  return FILE_SNIPPETS;
}
