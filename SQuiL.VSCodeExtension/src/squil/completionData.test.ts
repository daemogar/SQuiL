import { test } from 'node:test';
import * as assert from 'node:assert';
import {
  HEADER_VARS,
  SQLITE_HEADER_VARS,
  FILE_SNIPPETS,
  SQLITE_FILE_SNIPPETS,
  SQL_TYPES,
  SQLITE_TYPES,
  headerVarsFor,
  fileSnippetsFor,
  typesFor,
} from './completionData';
import { resolveProjectDialect } from './contextResolver';

// Task B (Phase 3B): dialect-aware completion. A SQLite `.squil` file must be offered
// `Create Temp Table` header/file snippets + the SQLite type vocabulary, NOT the T-SQL
// `Declare @…` / `Use` forms. A SQL Server file must be UNCHANGED. VS Code is the
// test-covered reference surface for the two C# ports.

// ─── header-var selection ──────────────────────────────────────────────────

test('headerVarsFor(sqlserver) is the unchanged T-SQL set', () => {
  assert.strictEqual(headerVarsFor('sqlserver'), HEADER_VARS);
  // Sanity: the T-SQL set still uses the @-sigil Declare forms.
  assert.ok(HEADER_VARS.every(v => v.prefix.startsWith('@')));
  assert.ok(HEADER_VARS.some(v => v.snippet.includes('@Param_')));
});

test('headerVarsFor(sqlite) offers Create Temp Table forms, never Declare @…', () => {
  const vars = headerVarsFor('sqlite');
  assert.strictEqual(vars, SQLITE_HEADER_VARS);
  assert.ok(vars.length >= 4, 'expected at least the 4 direction/cardinality forms');
  for (const v of vars) {
    assert.match(v.snippet, /Create Temp Table/i, `snippet must be a Create Temp Table form: ${v.snippet}`);
    assert.doesNotMatch(v.snippet, /Declare\s+@/i, `SQLite snippet must not use Declare @: ${v.snippet}`);
    assert.doesNotMatch(v.snippet, /@/, `SQLite has no @ sigil: ${v.snippet}`);
  }
});

test('sqlite header set covers Param_/Params_/Return_/Returns_ direction+cardinality', () => {
  const joined = SQLITE_HEADER_VARS.map(v => v.snippet).join('\n');
  for (const token of ['Param_', 'Params_', 'Return_', 'Returns_']) {
    assert.ok(joined.includes(`Create Temp Table ${token}`), `missing Create Temp Table ${token}`);
  }
});

// ─── file-snippet selection ────────────────────────────────────────────────

test('fileSnippetsFor(sqlserver) is the unchanged T-SQL set (keeps Use […])', () => {
  assert.strictEqual(fileSnippetsFor('sqlserver'), FILE_SNIPPETS);
  const scaffold = FILE_SNIPPETS.find(s => s.label === 'squil-file');
  assert.ok(scaffold, 'expected a squil-file scaffold');
  assert.match(scaffold!.snippet, /Use \[/i, 'T-SQL scaffold keeps its Use statement');
});

test('fileSnippetsFor(sqlite) scaffold uses Create Temp Table and has NO Use line', () => {
  const snippets = fileSnippetsFor('sqlite');
  assert.strictEqual(snippets, SQLITE_FILE_SNIPPETS);
  const scaffold = snippets.find(s => s.label === 'squil-file');
  assert.ok(scaffold, 'expected a squil-file scaffold');
  assert.match(scaffold!.snippet, /Create Temp Table/i, 'SQLite scaffold uses Create Temp Table');
  assert.doesNotMatch(scaffold!.snippet, /\bUse\s+\[/i, 'SQLite has no USE statement');
  // Every SQLite declaration snippet is a Create Temp Table form.
  for (const s of snippets) {
    assert.match(s.snippet, /Create Temp Table/i, `SQLite file snippet must be Create Temp Table: ${s.label}`);
    assert.doesNotMatch(s.snippet, /Declare\s+@/i, `SQLite file snippet must not use Declare @: ${s.label}`);
  }
});

// ─── type-vocabulary selection ─────────────────────────────────────────────

test('typesFor(sqlite) is the SQLite vocabulary; typesFor(sqlserver) the T-SQL one', () => {
  assert.strictEqual(typesFor('sqlite'), SQLITE_TYPES);
  assert.strictEqual(typesFor('sqlserver'), SQL_TYPES);
  for (const t of ['integer', 'text', 'real', 'blob']) {
    assert.ok(SQLITE_TYPES.includes(t), `SQLite types must include ${t}`);
  }
  assert.ok(SQL_TYPES.some(t => t.startsWith('varchar')), 'T-SQL types keep varchar');
  assert.ok(!SQLITE_TYPES.some(t => t.startsWith('varchar')), 'SQLite types drop varchar');
});

// ─── end-to-end: document → resolved dialect → offered snippets ─────────────

function makeReadFile(files: Record<string, string>): (p: string) => string | undefined {
  return (p: string) => files[p.replace(/\\/g, '/')];
}
function makeListDir(files: Record<string, string>): (d: string) => string[] {
  return (d: string) => {
    const dir = d.replace(/\\/g, '/').replace(/\/$/, '');
    const result = new Set<string>();
    for (const rawKey of Object.keys(files)) {
      const f = rawKey.replace(/\\/g, '/');
      if (!f.startsWith(dir + '/')) continue;
      const rest = f.slice(dir.length + 1);
      const nextSlash = rest.indexOf('/');
      result.add(nextSlash === -1 ? rest : rest.slice(0, nextSlash));
    }
    return Array.from(result);
  };
}

test('a SQLite-project document resolves to Create Temp Table snippets + SQLite types', () => {
  const files: Record<string, string> = {
    '/proj/Queries/People.squil': 'Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT);',
    '/proj/proj.csproj': '<Project><ItemGroup><PackageReference Include="SQuiL.Sqlite" Version="1.0.0" /></ItemGroup></Project>',
  };
  const dialect = resolveProjectDialect('/proj/Queries/People.squil', makeReadFile(files), makeListDir(files));
  assert.strictEqual(dialect, 'sqlite');

  assert.match(headerVarsFor(dialect)[0].snippet, /Create Temp Table/i);
  assert.doesNotMatch(fileSnippetsFor(dialect).find(s => s.label === 'squil-file')!.snippet, /\bUse\s+\[/i);
  assert.ok(typesFor(dialect).includes('integer'));
});

test('a SQL-Server-project document resolves to the unchanged T-SQL completions', () => {
  const files: Record<string, string> = {
    '/proj/Queries/People.squil': 'Declare @Param_Name varchar(100);',
    '/proj/proj.csproj': '<Project><ItemGroup><PackageReference Include="SQuiL.SqlServer" Version="1.0.0" /></ItemGroup></Project>',
  };
  const dialect = resolveProjectDialect('/proj/Queries/People.squil', makeReadFile(files), makeListDir(files));
  assert.strictEqual(dialect, 'sqlserver');

  assert.strictEqual(headerVarsFor(dialect), HEADER_VARS);
  assert.strictEqual(fileSnippetsFor(dialect), FILE_SNIPPETS);
  assert.strictEqual(typesFor(dialect), SQL_TYPES);
});
