import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';

// Task 5 (Phase 3B): editor parity for the SQLite header model. When the resolved dialect is
// 'sqlite', `parseSQuiL` recognizes `Create Temp Table <Prefix>_<Name> (...)` declarations
// (direction/cardinality from the bare name, single-column singular collapsing to a scalar)
// instead of the T-SQL `Declare @...` / `Use` form — mirroring the generator's SQuiLParser.

test('Params_ multi-column is an input list', () => {
  const r = parseSQuiL([
    'Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);',
    'Select 1;',
  ].join('\n'), 'sqlite');

  const v = r.variables.find(x => (x.role === 'params'));
  assert.ok(v, 'expected a params (input list) variable');
  assert.strictEqual(v!.name, 'Person');
  assert.strictEqual(v!.columns?.length, 3);
  assert.strictEqual(v!.columns![0].isPrimaryKey, true);
});

test('Param_ multi-column is an input object', () => {
  const r = parseSQuiL([
    'Create Temp Table Param_Address (Street TEXT, City TEXT);',
    'Select 1;',
  ].join('\n'), 'sqlite');

  const v = r.variables.find(x => x.role === 'param-table');
  assert.ok(v, 'expected a param-table (input object) variable');
  assert.strictEqual(v!.name, 'Address');
  assert.strictEqual(v!.columns?.length, 2);
});

test('Param_ single-column collapses to an input scalar', () => {
  const r = parseSQuiL([
    'Create Temp Table Param_Age (Age INTEGER);',
    'Select 1;',
  ].join('\n'), 'sqlite');

  const v = r.variables.find(x => x.role === 'param');
  assert.ok(v, 'expected a scalar param variable');
  assert.strictEqual(v!.name, 'Age');
  assert.strictEqual(v!.columns, undefined);
  assert.match(v!.sqlType, /INTEGER/i);
});

test('Returns_ single-column stays an output list (not a scalar)', () => {
  const r = parseSQuiL([
    'Create Temp Table Returns_ID (ID INTEGER);',
    'Select 1;',
  ].join('\n'), 'sqlite');

  const v = r.variables.find(x => x.role === 'returns');
  assert.ok(v, 'expected an output list variable');
  assert.strictEqual(v!.name, 'ID');
  assert.strictEqual(v!.columns?.length, 1);
});

test('Return_ single-column collapses to an output scalar', () => {
  const r = parseSQuiL([
    'Create Temp Table Return_Total (Total INTEGER);',
    'Select 1;',
  ].join('\n'), 'sqlite');

  const v = r.variables.find(x => x.role === 'return');
  assert.ok(v, 'expected a scalar return variable');
  assert.strictEqual(v!.name, 'Total');
  assert.strictEqual(v!.columns, undefined);
});

test('Return_ multi-column stays an output object', () => {
  const r = parseSQuiL([
    'Create Temp Table Return_Summary (RowCount INTEGER, Total INTEGER);',
    'Select 1;',
  ].join('\n'), 'sqlite');

  const v = r.variables.find(x => x.role === 'return-table');
  assert.ok(v, 'expected an output object variable');
  assert.strictEqual(v!.name, 'Summary');
  assert.strictEqual(v!.columns?.length, 2);
});

test('no "Missing USE" warning for the SQLite dialect', () => {
  const r = parseSQuiL([
    'Create Temp Table Return_Total (Total INTEGER);',
    'Select 1;',
  ].join('\n'), 'sqlite');

  const useWarnings = r.diagnostics.filter(d => /USE/i.test(d.message));
  assert.deepStrictEqual(useWarnings, [], 'SQLite has no USE statement — must not warn');
});

test('a full SQLite query recognizes both declarations across body/sample-DML lines', () => {
  const r = parseSQuiL([
    '--Name: ListParam_and_ListReturn',
    'Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);',
    'Create Temp Table Returns_Imported (PersonID INTEGER, Status TEXT);',
    "Insert Into Params_Person (PersonID, Name, Age) Values (1, 'Ada', 36);",
    "Insert Into Returns_Imported (PersonID, Status) Select PersonID, 'ok' From Params_Person;",
    'Select PersonID, Status From Returns_Imported;',
  ].join('\n'), 'sqlite');

  assert.strictEqual(r.queryName, 'ListParam_and_ListReturn');
  assert.strictEqual(r.variables.filter(v => v.role === 'params' || v.role === 'returns').length, 2);
  // Body / sample-DML lines must not be mistaken for declarations.
  assert.strictEqual(r.variables.length, 2);
});

// Residual #3 gap (full parity): Task A only fixed the body-BOUNDARY regex to recognize
// bracket-quoted table names. The separate declaration-PARSE regex above still used bare `\w+`
// and did not accept/strip brackets, so a FULLY bracket-quoted declaration was never recorded as
// a variable at all — diverging from the generator's IdentifierRegex, which strips brackets on
// both the declaration name and DML targets.
test('bracket-quoted table name is parsed into a variable with brackets stripped (full #3 parity)', () => {
  const r = parseSQuiL([
    'Create Temp Table [Params_Foo] (ID INTEGER Primary Key, Note TEXT);',
    'Select 1;',
  ].join('\n'), 'sqlite');

  const v = r.variables.find(x => x.role === 'params');
  assert.ok(v, 'expected a params (input list) variable parsed from the bracket-quoted declaration');
  assert.strictEqual(v!.name, 'Foo');
  assert.strictEqual(v!.rawName, 'Params_Foo', 'brackets must be stripped from rawName, matching the generator IdentifierRegex');
  assert.strictEqual(v!.columns?.length, 2);
});

test('the default (sqlserver) dialect does NOT parse Create Temp Table as a declaration', () => {
  const r = parseSQuiL([
    'Create Temp Table Params_Person (PersonID INTEGER, Name TEXT);',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(r.variables.length, 0, 'Create Temp Table is inert under the SQL Server dialect');
});
