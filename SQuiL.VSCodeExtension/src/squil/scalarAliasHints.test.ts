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

// ── SP0042 scanner regressions, mirroring ScalarSelectAliaserTests.cs ───────
//
// These four pin the exact behaviors the C# ground truth pins with a committed test
// (Rewrite_ignores_a_select_inside_a_nested_block_comment,
// Rewrite_aliases_a_live_select_following_a_nested_block_comment_containing_an_apostrophe,
// Rewrite_leaves_an_already_bracketed_alias_alone,
// Rewrite_bare_select_followed_by_another_statement_without_semicolon) so the two suites
// can be diffed by a human. Unlike the C# `Rewrite` tests (which assert on the REWRITTEN
// text), these assert on `scalarAliasHints`'s hint COUNT — the editor-side observable.

test('SP0042 stays silent on a select hidden inside a nested block comment (mirrors Rewrite_ignores_a_select_inside_a_nested_block_comment)', () => {
  const text = [
    'Declare @Return_Count int;',
    'Use Db;',
    '/* /* nested */ Select @Return_Count; */',
  ].join('\n');
  assert.strictEqual(
    scalarAliasHints(parseSQuiL(text), text, 'sqlserver').length,
    0,
    'a select entirely inside a NESTED block comment must not be seen as live code',
  );
});

test("SP0042 still fires on the live select after a nested block comment containing an apostrophe (mirrors Rewrite_aliases_a_live_select_following_a_nested_block_comment_containing_an_apostrophe)", () => {
  const text = [
    'Declare @Return_Count int;',
    'Use Db;',
    "/* /* nested */ it's dead */ Select @Return_Count;",
  ].join('\n');
  const hints = scalarAliasHints(parseSQuiL(text), text, 'sqlserver');
  assert.strictEqual(
    hints.length,
    1,
    "the apostrophe in \"it's\" (inside the comment) must not open a phantom string literal that swallows the live select following the comment's TRUE close",
  );
  assert.strictEqual(hints[0].declaredName, 'Count');
});

test('SP0042 stays silent on an already-bracketed alias (mirrors Rewrite_leaves_an_already_bracketed_alias_alone)', () => {
  const text = ['Declare @Return_Count int;', 'Use Db;', 'Select @Return_Count As [Count];'].join('\n');
  assert.strictEqual(
    scalarAliasHints(parseSQuiL(text), text, 'sqlserver').length,
    0,
    'a bracketed alias must be recognized as ALREADY ALIASED, or an already-fixed file gets re-hinted (and a second accepted fix would double-alias)',
  );
});

test('SP0042 fires on a bare select followed by another statement with no semicolon (mirrors Rewrite_bare_select_followed_by_another_statement_without_semicolon)', () => {
  const text = [
    'Declare @Return_Count int;',
    'Use Db;',
    'Select @Return_Count',
    'Update T Set X = 1',
  ].join('\n');
  const hints = scalarAliasHints(parseSQuiL(text), text, 'sqlserver');
  assert.strictEqual(hints.length, 1, 'a statement-starter keyword (no semicolon) must still terminate the bare select');
  assert.strictEqual(hints[0].declaredName, 'Count');
});
