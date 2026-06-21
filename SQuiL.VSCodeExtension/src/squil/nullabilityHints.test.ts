import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { nullabilityHints } from './nullabilityHints';

// ── helper ─────────────────────────────────────────────────────────────────

function collectHints(sql: string) {
  return nullabilityHints(parseSQuiL(sql));
}

// ── Tests ──────────────────────────────────────────────────────────────────

test('hint fires on unmarked scalar param, silent on null-marked scalar', () => {
  const hints = collectHints([
    'Declare @Param_A int;',         // unmarked → hint
    'Declare @Param_B int null;',    // marked NULL → no hint
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(hints.length, 1);
  assert.strictEqual(hints[0].code, 'SP0010');
  assert.ok(hints[0].message.includes('`int A`'), `message should include '\`int A\`'; got: ${hints[0].message}`);
});

test('hint fires on unmarked return scalar, silent on not-null-marked', () => {
  const hints = collectHints([
    'Declare @Return_Count int;',         // unmarked → hint
    'Declare @Return_Name varchar(50) not null;', // marked NOT NULL → no hint
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(hints.length, 1);
  assert.strictEqual(hints[0].code, 'SP0010');
  assert.ok(hints[0].message.includes('`int Count`'), `message should include '\`int Count\`'; got: ${hints[0].message}`);
});

test('hint fires on unmarked table column, silent on marked columns', () => {
  const hints = collectHints([
    'Declare @Params_T table(X int, Y int not null);',  // X → hint, Y → no hint
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(hints.length, 1);
  assert.strictEqual(hints[0].code, 'SP0010');
  assert.ok(hints[0].message.includes('`int X`'), `message should include '\`int X\`'; got: ${hints[0].message}`);
});

test('hint fires on scalar and column together', () => {
  const hints = collectHints([
    'Declare @Param_A int;',                            // scalar unmarked → hint
    'Declare @Param_B int null;',                       // marked → no hint
    'Declare @Params_T table(X int, Y int not null);',  // X → hint, Y → no hint
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(hints.length, 2);
  assert.ok(hints.every(h => h.code === 'SP0010'), 'all should be SP0010');
});

test('no hint for specials (debug, environmentName, asOfDate, etc.)', () => {
  const hints = collectHints([
    'Declare @Debug bit;',
    'Declare @SuppressDebug bit;',
    'Declare @EnvironmentName varchar(50);',
    "Declare @AsOfDate date = '2008-01-01';",
    'Declare @Error varchar(500);',
    'Declare @Errors varchar(max);',
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(hints.length, 0, `expected no hints for specials, got ${hints.length}`);
});

test('no hint for params/returns table-valued vars (hint lives on their columns)', () => {
  // @Params_T is role 'params' — its column X has no marker so it gets a hint;
  // the variable-level (params) itself must NOT get an additional hint.
  const hints = collectHints([
    'Declare @Params_T table(X int not null);',  // column marked → no hint anywhere
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(hints.length, 0);
});

test('hint message format is exact', () => {
  const hints = collectHints([
    'Declare @Param_Name varchar(100);',
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(hints.length, 1);
  assert.strictEqual(
    hints[0].message,
    'No `null`/`not null` marker — generated C# is non-nullable `string Name`. Add `not null` to confirm, or `null` to make it nullable.',
  );
});
