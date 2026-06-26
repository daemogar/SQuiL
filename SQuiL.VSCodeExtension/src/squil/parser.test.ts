import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';

// Recognition parity with the generator's SQuiLParser: bare @SuppressDebug and
// @AsOfDate are valid header specials and must NOT raise a "doesn't follow
// SQuiL naming conventions" (must-be-prefixed) warning.

test('bare @SuppressDebug and @AsOfDate are recognized header specials (no naming warning)', () => {
  const result = parseSQuiL([
    '--Name: AsOf',
    'Declare @Debug bit = 1;',
    'Declare @SuppressDebug bit = 0;',
    "Declare @AsOfDate date = '2008-10-01';",
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Set @Return_Count = (Select Count(*) From Logs Where CreatedOn <= @AsOfDate);',
    'Select @Return_Count;',
  ].join('\n'));

  // No "doesn't follow SQuiL naming conventions" warnings for the specials.
  const namingWarnings = result.diagnostics.filter((d) =>
    d.message.includes("doesn't follow SQuiL naming conventions"));
  assert.deepStrictEqual(namingWarnings, []);

  const suppress = result.variables.find((v) => v.rawName === '@SuppressDebug');
  assert.ok(suppress, '@SuppressDebug should be parsed');
  assert.strictEqual(suppress!.role, 'suppressDebug');

  const asOf = result.variables.find((v) => v.rawName === '@AsOfDate');
  assert.ok(asOf, '@AsOfDate should be parsed');
  assert.strictEqual(asOf!.role, 'asOfDate');
});

// Task 5: flip column nullable polarity (non-nullable by default) and capture
// nullabilityMarker on both columns and scalars.
test('column nullable only with explicit NULL; scalar marker captured', () => {
  const r = parseSQuiL([
    'Declare @Params_X table(A int, B varchar(50) null, C varchar(50) not null);',
    'Declare @Param_S int;',
    'Declare @Param_SN int null;',
    'Use Db;', 'Select 1;',
  ].join('\n'));
  const x = r.variables.find(v => v.name === 'X')!;
  assert.deepStrictEqual(x.columns!.map(c => c.nullable), [false, true, false]);
  assert.deepStrictEqual(x.columns!.map(c => c.nullabilityMarker), [undefined, 'NULL', 'NOT NULL']);
  assert.strictEqual(x.nullabilityMarker, undefined, 'table var must not inherit nullabilityMarker from column text');
  assert.strictEqual(r.variables.find(v => v.name === 'S')!.nullabilityMarker, undefined);
  assert.strictEqual(r.variables.find(v => v.name === 'SN')!.nullabilityMarker, 'NULL');
});

// SP0017: shape mismatch between two same-name table variables in one file.
test('SP0017 shape mismatch: same name different columns fires error with relatedInformation', () => {
  const sql = [
    '--Name: ShapeMismatch',
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Declare @Return_Person table(PersonID int, Age int);',
    'Use [Database];',
    'Select * From @Returns_Person;',
    'Select * From @Return_Person;',
  ].join('\n');

  const result = parseSQuiL(sql);
  const sp0017 = result.diagnostics.filter(d => d.code === 'SP0017');
  assert.strictEqual(sp0017.length, 1, 'should emit exactly one SP0017 diagnostic');
  assert.ok(sp0017[0].relatedLine !== undefined, 'should have relatedLine pointing at first declaration');
  assert.strictEqual(sp0017[0].relatedLine, 1, 'first declaration is on line 1');
});

// Parity with the generator: a column DEFAULT must be parsed (not dropped) and
// its raw literal captured, so the preview can render it as an init-property default.
test('table column DEFAULT is parsed and its literal captured', () => {
  const result = parseSQuiL([
    '--Name: Defaults',
    "Declare @Params_Rows table(RowID int, Amount decimal(18,2) default 1.5, Note varchar(50) default 'hello');",
    'Use MyDatabase;',
    'Select 1;',
  ].join('\n'));

  const rows = result.variables.find((v) => v.rawName === '@Params_Rows');
  assert.ok(rows, '@Params_Rows should be parsed');
  // All three columns survive parsing (defaulted columns are not dropped).
  assert.strictEqual(rows!.columns?.length, 3);
  assert.strictEqual(rows!.columns![0].defaultValue, undefined);
  assert.strictEqual(rows!.columns![1].defaultValue, '1.5');
  assert.strictEqual(rows!.columns![2].defaultValue, "'hello'");
});
