import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL, sqliteBodyStartLine } from './parser';
import { scanMutations } from './mutationScanner';

// Full-path integration tests for the Task-9 review fix (dialect-aware mutation-lint body
// extraction for USE-less SQLite files). These parse a REAL SQLite query STRING through the
// same entry point the diagnostics provider uses — parseSQuiL(text, 'sqlite') then the
// dialect-aware body extraction (sqliteBodyStartLine) — and run scanMutations on the REAL
// extracted body. This deliberately does NOT hand a literal body to scanMutations, because
// that is exactly what masked the bug: the provider derived the body from `databaseLine`,
// which is undefined for a USE-less SQLite file, so the body came out empty. With an empty
// body the SP0025 SQLite `Begin` regex was dead and a legit SQLite mutation drew a spurious
// SP0024.
//
// Mirror of the C# LintMutationDiagnostics SQLite path (SQuiLLinter.cs) — change one, change all.

/** Replicates the diagnostics provider's dialect-aware body extraction (diagnosticsProvider.ts). */
function extractBody(text: string, dialect: 'sqlserver' | 'sqlite'): string {
  const parsed = parseSQuiL(text, dialect);
  const lines = text.split('\n');
  let bodyStartLine: number;
  if (dialect === 'sqlite') {
    bodyStartLine = sqliteBodyStartLine(text, parsed);
  } else {
    const dl = parsed.databaseLine ?? -1;
    bodyStartLine = dl >= 0 ? dl + 1 : -1;
  }
  if (bodyStartLine < 0 || bodyStartLine >= lines.length) return '';
  return lines.slice(bodyStartLine).join('\n');
}

// A real SQLite [SQuiLQueryTransaction] file: no USE, Create-Temp-Table header, a body that
// opens its own transaction with a bare `Begin` and mutates a real table.
const sqliteBeginFile = [
  'Create Temp Table Params_Widget (WidgetID INTEGER Primary Key, Name TEXT);',
  'Begin;',
  'Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;',
  'Commit;',
].join('\n');

// A real SQLite [SQuiLQueryTransaction] file that legitimately mutates a real table but has
// NO own transaction (the C# DbTransaction wraps it) — must not be flagged read-only (SP0024).
const sqliteMutationFile = [
  'Create Temp Table Params_Widget (WidgetID INTEGER Primary Key, Name TEXT);',
  'Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;',
].join('\n');

// ── SP0025: SQLite bare Begin is detected end-to-end ──────────────────────────

test('SP0025 SQLite full path: bare Begin in a USE-less file is detected as an own transaction', () => {
  const body = extractBody(sqliteBeginFile, 'sqlite');
  assert.ok(body.length > 0, 'the SQLite body must not be empty after dialect-aware extraction');
  assert.ok(/\bBegin\b/i.test(body), 'the extracted body must include the Begin statement');
  const scan = scanMutations(body, 'sqlite');
  assert.strictEqual(scan.hasOwnTransaction, true, 'SP0025 must fire: body has its own Begin');
});

// Documents the pre-fix bug precisely: a USE-less SQLite file has no databaseLine, so the old
// databaseLine-derived body extraction produced an EMPTY body and the Begin was invisible.
test('SP0025 SQLite: pre-fix databaseLine extraction produced an empty body that missed the Begin', () => {
  const parsed = parseSQuiL(sqliteBeginFile, 'sqlite');
  assert.strictEqual(parsed.databaseLine, undefined, 'no USE → no databaseLine');
  const preFixBody = ''; // databaseLine undefined ⇒ old provider code left bodyText = ''
  assert.strictEqual(
    scanMutations(preFixBody, 'sqlite').hasOwnTransaction,
    false,
    'the pre-fix empty body could never detect the Begin (this is what the fix repairs)',
  );
});

// ── SP0024: legit SQLite mutation is not spuriously flagged read-only ─────────

test('SP0024 SQLite full path: a legit real-table mutation is NOT provably read-only (no spurious SP0024)', () => {
  const body = extractBody(sqliteMutationFile, 'sqlite');
  const scan = scanMutations(body, 'sqlite');
  assert.strictEqual(scan.isProvablyReadOnly, false, 'a real-table Insert is a mutation, not read-only');
  assert.ok(scan.mutations.some((m) => m.kind === 'Insert'), 'the Insert into the real table must be recorded');
});

test('SP0024 SQLite: pre-fix databaseLine extraction wrongly reported the body read-only (spurious SP0024)', () => {
  const parsed = parseSQuiL(sqliteMutationFile, 'sqlite');
  assert.strictEqual(parsed.databaseLine, undefined, 'no USE → no databaseLine');
  const preFixBody = ''; // databaseLine undefined ⇒ old provider code left bodyText = ''
  assert.strictEqual(
    scanMutations(preFixBody, 'sqlite').isProvablyReadOnly,
    true,
    'the pre-fix empty body looked read-only, drawing a spurious SP0024 (this is what the fix repairs)',
  );
});

// ── Body-boundary correctness (mirrors the generator tokenizer's Task-5 boundary) ──

test('sqliteBodyStartLine skips leading multi-declaration header (ImportPeople shape)', () => {
  const text = [
    'Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);',
    'Create Temp Table Returns_Imported (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);',
    'Insert Into Returns_Imported (PersonID, Name, Age) Select PersonID, Name, Age From Params_Person;',
    'Select PersonID, Name, Age From Returns_Imported;',
  ].join('\n');
  const parsed = parseSQuiL(text, 'sqlite');
  // Body begins at line index 2 — the first statement that is neither a Create Temp Table
  // declaration nor a param-table population (Insert Into Returns_Imported is real body logic).
  assert.strictEqual(sqliteBodyStartLine(text, parsed), 2);
});

test('sqliteBodyStartLine skips a multi-line Create Temp Table block', () => {
  const text = [
    'Create Temp Table Params_Widget (',
    '  WidgetID INTEGER Primary Key,',
    '  Amount NUMERIC,',
    '  Name TEXT);',
    'Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;',
  ].join('\n');
  const parsed = parseSQuiL(text, 'sqlite');
  assert.strictEqual(sqliteBodyStartLine(text, parsed), 4);
});

test('sqliteBodyStartLine skips a leading bare-name param-table population statement', () => {
  const text = [
    'Create Temp Table Params_Widget (WidgetID INTEGER Primary Key, Name TEXT);',
    "Insert Into Params_Widget (WidgetID, Name) Values (1, 'x');",
    'Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;',
  ].join('\n');
  const parsed = parseSQuiL(text, 'sqlite');
  // Line 1 populates the param table (sample data) and is skipped; the body is the real
  // Insert Into Widgets on line 2.
  assert.strictEqual(sqliteBodyStartLine(text, parsed), 2);
  const body = extractBody(text, 'sqlite');
  assert.ok(/Insert\s+Into\s+Widgets/i.test(body));
  assert.ok(!/Insert\s+Into\s+Params_Widget/i.test(body), 'the param-table population must NOT be in the body');
});

// SQL Server behavior must be unchanged: sqliteBodyStartLine is never used for T-SQL.
test('SQL Server body extraction still uses USE line (unchanged)', () => {
  const text = [
    'Declare @Param_Name varchar(50);',
    'Use MyDb;',
    'Update dbo.Widgets set Name = @Param_Name;',
  ].join('\n');
  const body = extractBody(text, 'sqlserver');
  const scan = scanMutations(body, 'sqlserver');
  assert.strictEqual(scan.isProvablyReadOnly, false);
  assert.ok(scan.mutations.some((m) => m.kind === 'Update'));
});
