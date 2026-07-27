import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL, lintParamsBeforeReturns } from './parser';

// SP0040 (Phase 3B, Task 7): every @Param/@Params (input) must be declared before any
// @Return/@Returns (output). Severity is dialect-dependent — ERROR for SQLite (its
// Create-Temp-Table header must create inputs before the shred reads them), WARNING otherwise.
// Mirrors SQuiLOrderingValidator.cs (generator) and LintParamsBeforeReturns in SQuiLLinter.cs.

test('SQL Server: output before input is an SP0040 warning', () => {
  const parsed = parseSQuiL(
    ['Declare @Return_A int;', 'Declare @Param_B int;', 'Use [Db];', 'Select 1;'].join('\n'),
    'sqlserver',
  );
  const sp = parsed.diagnostics.filter(d => d.code === 'SP0040');
  assert.strictEqual(sp.length, 1);
  assert.strictEqual(sp[0].severity, 'warning');
});

test('SQLite: output before input is an SP0040 error', () => {
  const parsed = parseSQuiL(
    ['Create Temp Table Return_A (Value INTEGER);', 'Create Temp Table Param_B (Value INTEGER);', 'Select 1;'].join('\n'),
    'sqlite',
  );
  const sp = parsed.diagnostics.filter(d => d.code === 'SP0040');
  assert.strictEqual(sp.length, 1);
  assert.strictEqual(sp[0].severity, 'error');
});

test('inputs before outputs — no SP0040', () => {
  const parsed = parseSQuiL(
    ['Declare @Param_B int;', 'Declare @Return_A int;', 'Use [Db];', 'Select 1;'].join('\n'),
    'sqlserver',
  );
  assert.strictEqual(parsed.diagnostics.filter(d => d.code === 'SP0040').length, 0);
});

test('outputs only — no SP0040 (rule needs at least one input)', () => {
  const parsed = parseSQuiL(
    ['Declare @Return_A int;', 'Declare @Returns_B table(ID int);', 'Use [Db];', 'Select 1;'].join('\n'),
    'sqlserver',
  );
  assert.strictEqual(parsed.diagnostics.filter(d => d.code === 'SP0040').length, 0);
});

test('reported once, anchored at the first offending output', () => {
  const diags = lintParamsBeforeReturns(
    parseSQuiL(
      ['Declare @Return_A int;', 'Declare @Return_C int;', 'Declare @Param_B int;', 'Use [Db];', 'Select 1;'].join('\n'),
      'sqlserver',
    ),
    'sqlserver',
  );
  assert.strictEqual(diags.length, 1);
  // first offending output = @Return_A on line 0 (0-based)
  assert.strictEqual(diags[0].line, 0);
});
