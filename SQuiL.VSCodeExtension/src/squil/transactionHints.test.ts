import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { transactionHints } from './transactionHints';
import type { ResolvedContext } from './contextResolver';

// ── helpers ──────────────────────────────────────────────────────────────────

/** A resolved context simulating [SQuiLQueryTransaction] with debugRollback:true (default). */
function txnContext(overrides: Partial<ResolvedContext> = {}): ResolvedContext {
  return {
    found: true,
    matchCount: 1,
    attribute: 'SQuiLQueryTransaction',
    enabled: true,
    debugRollback: true,
    ...overrides,
  };
}

/** A resolved context simulating [SQuiLQuery] (no transaction). */
function queryContext(overrides: Partial<ResolvedContext> = {}): ResolvedContext {
  return {
    found: true,
    matchCount: 1,
    attribute: 'SQuiLQuery',
    enabled: false,
    debugRollback: true,
    ...overrides,
  };
}

function collectHints(sql: string, ctx: ResolvedContext) {
  return transactionHints(parseSQuiL(sql), ctx);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

test('SP0026 fires: SQuiLQueryTransaction + debugRollback:true + no @Debug', () => {
  const hints = collectHints([
    'Declare @Param_Id int not null;',
    'Use [Database];',
    'Update [Docs] Set Flag = 1 Where Id = @Param_Id;',
  ].join('\n'), txnContext());

  assert.strictEqual(hints.length, 1);
  assert.strictEqual(hints[0].code, 'SP0026');
  assert.ok(
    hints[0].message.includes('`debugRollback: true` has no effect'),
    `unexpected message: ${hints[0].message}`,
  );
  assert.strictEqual(hints[0].line, 0);
  assert.strictEqual(hints[0].character, 0);
});

test('SP0026 silent: SQuiLQueryTransaction + debugRollback:true + @Debug declared', () => {
  const hints = collectHints([
    'Declare @Debug bit;',
    'Declare @Param_Id int not null;',
    'Use [Database];',
    'Update [Docs] Set Flag = 1 Where Id = @Param_Id;',
  ].join('\n'), txnContext());

  assert.strictEqual(hints.length, 0, 'no SP0026 when @Debug is declared');
});

test('SP0026 silent: SQuiLQueryTransaction + debugRollback:false', () => {
  const hints = collectHints([
    'Declare @Param_Id int not null;',
    'Use [Database];',
    'Update [Docs] Set Flag = 1 Where Id = @Param_Id;',
  ].join('\n'), txnContext({ debugRollback: false }));

  assert.strictEqual(hints.length, 0, 'no SP0026 when debugRollback is explicitly false');
});

test('SP0026 silent: SQuiLQuery (never a transaction — debugRollback irrelevant)', () => {
  const hints = collectHints([
    'Declare @Param_Id int not null;',
    'Use [Database];',
    'Select * From [Docs] Where Id = @Param_Id;',
  ].join('\n'), queryContext());

  assert.strictEqual(hints.length, 0, 'no SP0026 for [SQuiLQuery]');
});

test('SP0026 silent: orphan context (not found)', () => {
  const orphan: ResolvedContext = {
    found: false,
    matchCount: 0,
    attribute: undefined,
    enabled: false,
    debugRollback: true,
  };
  const hints = collectHints([
    'Declare @Param_Id int not null;',
    'Use [Database];',
    'Update [Docs] Set Flag = 1 Where Id = @Param_Id;',
  ].join('\n'), orphan);

  assert.strictEqual(hints.length, 0, 'no SP0026 for orphan (not found)');
});

test('SP0026 fires exactly once even with multiple non-debug variables', () => {
  const hints = collectHints([
    'Declare @Param_A int not null;',
    'Declare @Param_B varchar(50) not null;',
    'Use [Database];',
    'Update [Docs] Set Name = @Param_B Where Id = @Param_A;',
  ].join('\n'), txnContext());

  assert.strictEqual(hints.length, 1, 'only one SP0026 hint regardless of variable count');
});

test('SP0026 message format is exact', () => {
  const hints = collectHints([
    'Declare @Param_Id int not null;',
    'Use [Database];',
    'Update [Docs] Set Flag = 1 Where Id = @Param_Id;',
  ].join('\n'), txnContext());

  assert.strictEqual(hints.length, 1);
  assert.strictEqual(
    hints[0].message,
    '`debugRollback: true` has no effect without a declared `@Debug`. ' +
    'Declare `@Debug bit;` in the header, or set `debugRollback: false` on [SQuiLQueryTransaction].',
  );
});
