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
