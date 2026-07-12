import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL, lintCardinalityCollision, lintShapeCollision, lintUnmatchedSelect, lintTimestampInput } from './parser';
import { shapeHints } from './shapeHints';

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

// SP0017: same-name pair that differs ONLY in a column size must NOT fire —
// the generator's SameShape is size-independent (sizes may differ).
// This is a RED→GREEN regression guard added by the size-strip fix.
test('SP0017 silent when same-name tables differ only in column size', () => {
  const sql = [
    '--Name: SizeOnly',
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Declare @Return_Person  table(PersonID int, FullName varchar(50));',
    'Use [Database];',
    'Select 1;',
  ].join('\n');

  const result = parseSQuiL(sql);
  const sp0017 = result.diagnostics.filter(d => d.code === 'SP0017');
  assert.strictEqual(sp0017.length, 0, 'SP0017 must not fire when shapes differ only in column size');
});

// SP0022: cardinality collision (same name, list + single object, same side).
test('SP0022 fires on same-file output list + object with the same name', () => {
  const diags = lintCardinalityCollision(parseSQuiL([
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Declare @Return_Person table(PersonID int, FullName varchar(100));',
    'Use Db;',
    'Select 1;',
  ].join('\n')));

  // one warning on the first declaration, one error on the second
  assert.strictEqual(diags.length, 2);
  assert.ok(diags.every(d => d.code === 'SP0022'));
  const warning = diags.find(d => d.severity === 'warning');
  const error = diags.find(d => d.severity === 'error');
  assert.ok(warning, 'warning on the first declaration');
  assert.ok(error, 'error on the second declaration');
  assert.strictEqual(warning!.line, 0);
  assert.strictEqual(error!.line, 1);
  assert.strictEqual(error!.relatedLine, 0);
  // both squiggles name both variables in their text
  assert.ok(warning!.message.includes('@Returns_Person') && warning!.message.includes('@Return_Person'),
    'warning text names both the winner and the conflicting variable');
  assert.ok(error!.message.includes('@Return_Person') && error!.message.includes('@Returns_Person'),
    'error text names both variables');
});

test('SP0022 fires on same-file input list + object with the same name', () => {
  const diags = lintCardinalityCollision(parseSQuiL([
    'Declare @Params_Rows table(RowID int);',
    'Declare @Param_Rows table(RowID int);',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.strictEqual(diags.length, 2);
  assert.ok(diags.every(d => d.code === 'SP0022'));
});

test('SP0022 silent for same cardinality, same name', () => {
  const diags = lintCardinalityCollision(parseSQuiL([
    'Declare @Returns_Person table(PersonID int);',
    'Declare @Returns_Person table(PersonID int);',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.strictEqual(diags.length, 0);
});

test('SP0022 silent across sides (input list + output object)', () => {
  const diags = lintCardinalityCollision(parseSQuiL([
    'Declare @Params_Person table(PersonID int);',
    'Declare @Return_Person table(PersonID int);',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.strictEqual(diags.length, 0);
});

test('SP0022 flags only cardinality mismatch among 3+ same-name decls', () => {
  // @Returns_X (list) + @Return_X (object) + @Returns_X (list): the duplicate list
  // is a same-cardinality dedup, not a collision — only the object is flagged.
  const diags = lintCardinalityCollision(parseSQuiL([
    'Declare @Returns_X table(ID int);',
    'Declare @Return_X table(ID int);',
    'Declare @Returns_X table(ID int);',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.strictEqual(diags.length, 2); // 1 warning on the winner + 1 error on the object
  assert.ok(diags.every(d => d.code === 'SP0022'));
  assert.strictEqual(diags.filter(d => d.severity === 'warning').length, 1);
  assert.strictEqual(diags.filter(d => d.severity === 'error').length, 1);
});

// SP0030: result-shape collision (same-file same-side output pairs with identical signature)
test('SP0030 fires on two same-file outputs with identical signature', () => {
  const diags = lintShapeCollision(parseSQuiL([
    'Declare @Returns_Active table(PersonID int, Name varchar(100));',
    'Declare @Returns_Inactive table(PersonID int, Name varchar(100));',
    'Use Db;',
    'Select * From @Returns_Active;',
  ].join('\n')));
  assert.ok(diags.length >= 2, 'both declarations flagged');
  assert.ok(diags.every(d => d.code === 'SP0030'));
  assert.ok(diags[0].message.includes('@Returns_Active') && diags[0].message.includes('@Returns_Inactive'));
});

test('SP0030 fires on length-only difference (both string)', () => {
  const diags = lintShapeCollision(parseSQuiL([
    'Declare @Returns_A table(Note varchar(50));',
    'Declare @Returns_B table(Note varchar(100));',
    'Use Db;',
    'Select * From @Returns_A;',
  ].join('\n')));
  assert.ok(diags.length >= 2 && diags.every(d => d.code === 'SP0030'));
});

test('SP0020 does NOT also fire where SP0030 applies (no double squiggle)', () => {
  const parsed = parseSQuiL([
    'Declare @Returns_Active table(PersonID int, Name varchar(100));',
    'Declare @Returns_Inactive table(PersonID int, Name varchar(100));',
    'Use Db;',
    'Select * From @Returns_Active;',
  ].join('\n'));
  const s20 = shapeHints(parsed);
  assert.strictEqual(s20.length, 0, 'SP0020 suppressed for same-file same-side output collisions');
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

// SP0031: unmatched standalone SELECT — editor-only warning
test('SP0031 warns when a standalone SELECT column list matches no declared output', () => {
  const text = [
    'Declare @Returns_People table(PersonID int, Name varchar(100));',
    'Use Db;',
    'Select PersonID, WrongName From People;',   // "wrongname" != declared "name"
  ].join('\n');
  const parsed = parseSQuiL(text);
  const diags = lintUnmatchedSelect(parsed, text);
  assert.ok(diags.some(d => d.code === 'SP0031'), 'unmatched select flagged');
});

test('SP0031 stays silent when a standalone SELECT matches a declared output', () => {
  const text = [
    'Declare @Returns_People table(PersonID int, Name varchar(100));',
    'Use Db;',
    'Select PersonID, Name From People;',
  ].join('\n');
  assert.strictEqual(lintUnmatchedSelect(parseSQuiL(text), text).filter(d => d.code === 'SP0031').length, 0);
});

test('SP0031 ignores Select * and Insert Into', () => {
  const text = [
    'Declare @Returns_People table(PersonID int, Name varchar(100));',
    'Use Db;',
    'Insert Into @Returns_People Select PersonID, Name From People;',
    'Select * From @Returns_People;',
  ].join('\n');
  assert.strictEqual(lintUnmatchedSelect(parseSQuiL(text), text).filter(d => d.code === 'SP0031').length, 0);
});

// SP0032: timestamp/rowversion is server-generated and read-only — forbidden as an input.
test('SP0032 fires on a timestamp input scalar', () => {
  const diags = lintTimestampInput(parseSQuiL([
    'Declare @Param_V timestamp;',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.ok(diags.some(d => d.code === 'SP0032'), 'timestamp input flagged');
});

test('SP0032 stays silent on a timestamp output scalar', () => {
  const diags = lintTimestampInput(parseSQuiL([
    'Declare @Return_V timestamp;',
    'Use Db;',
    'Select @Return_V;',
  ].join('\n')));
  assert.strictEqual(diags.filter(d => d.code === 'SP0032').length, 0);
});

test('SP0032 fires on a timestamp input table column', () => {
  const diags = lintTimestampInput(parseSQuiL([
    'Declare @Params_Rows table(RowID int, Ver timestamp);',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.ok(diags.some(d => d.code === 'SP0032'), 'timestamp input table column flagged');
});
