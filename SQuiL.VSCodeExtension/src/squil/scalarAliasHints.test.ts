import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { scalarAliasHints } from './scalarAliasHints';

// ── SP0042: implicit scalar alias (editor-only hint) ────────────────────────

test('SP0042 hints on a bare scalar select', () => {
  const text = [
    'Declare @Return_Count int;',
    'Use Db;',
    'Select @Return_Count;',
  ].join('\n');
  const hints = scalarAliasHints(parseSQuiL(text), text, 'sqlserver');
  assert.strictEqual(hints.length, 1);
  assert.strictEqual(hints[0].code, 'SP0042');
  assert.strictEqual(hints[0].declaredName, 'Count');
});

test('SP0042 hints on a bare scalar select with no semicolon', () => {
  const text = ['Declare @Return_Count int;', 'Use Db;', 'Select @Return_Count'].join('\n');
  assert.strictEqual(scalarAliasHints(parseSQuiL(text), text, 'sqlserver').length, 1);
});

test('SP0042 stays silent when the alias is already written', () => {
  const text = ['Declare @Return_Count int;', 'Use Db;', 'Select @Return_Count As Count;'].join('\n');
  assert.strictEqual(scalarAliasHints(parseSQuiL(text), text, 'sqlserver').length, 0);
});

test('SP0042 stays silent on the assignment form', () => {
  const text = ['Declare @Return_Count int;', 'Use Db;', 'Select @Return_Count = 1;'].join('\n');
  assert.strictEqual(scalarAliasHints(parseSQuiL(text), text, 'sqlserver').length, 0);
});

test('SP0042 stays silent for temp-table dialects', () => {
  const text = ['Declare @Return_Count int;', 'Use Db;', 'Select @Return_Count;'].join('\n');
  assert.strictEqual(scalarAliasHints(parseSQuiL(text), text, 'sqlite').length, 0);
  assert.strictEqual(scalarAliasHints(parseSQuiL(text), text, 'postgres').length, 0);
});
