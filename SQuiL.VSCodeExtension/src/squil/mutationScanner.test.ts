import { test } from 'node:test';
import * as assert from 'node:assert';
import { scanMutations } from './mutationScanner';

// Mirrors MutationScannerTests.cs in SQuiL.Tests — change one side, change the other.

// ProvablyReadOnly theory rows
test('Select on real table is provably read-only', () => {
  assert.strictEqual(scanMutations('Select * from [Documents];').isProvablyReadOnly, true);
});

test('Insert into @table-var is provably read-only', () => {
  assert.strictEqual(scanMutations('Insert Into @Rows(Id) Values (1);').isProvablyReadOnly, true);
});

test('Update @table-var is provably read-only', () => {
  assert.strictEqual(scanMutations('Update @Rows set Id = 1;').isProvablyReadOnly, true);
});

// NotReadOnly_RecordsHit theory rows
test('Update real table is not read-only and records Update hit', () => {
  const r = scanMutations('Update [Documents] set X = 1;');
  assert.strictEqual(r.isProvablyReadOnly, false);
  assert.ok(r.mutations.some((m) => m.kind === 'Update'));
});

test('Insert Into real table is not read-only and records Insert hit', () => {
  const r = scanMutations('Insert Into [Documents](X) Values (1);');
  assert.strictEqual(r.isProvablyReadOnly, false);
  assert.ok(r.mutations.some((m) => m.kind === 'Insert'));
});

test('Delete From real table is not read-only and records Delete hit', () => {
  const r = scanMutations('Delete From dbo.Documents;');
  assert.strictEqual(r.isProvablyReadOnly, false);
  assert.ok(r.mutations.some((m) => m.kind === 'Delete'));
});

test('Merge Into real table is not read-only and records Merge hit', () => {
  const r = scanMutations('Merge Into Target using Src on 1=1;');
  assert.strictEqual(r.isProvablyReadOnly, false);
  assert.ok(r.mutations.some((m) => m.kind === 'Merge'));
});

// DetectsOwnTransaction
test('detects Begin Tran as own transaction', () => {
  assert.strictEqual(
    scanMutations('Begin Tran; Update [T] set X=1; Commit;').hasOwnTransaction,
    true
  );
});

// IgnoresMutationKeywordsInCommentsAndStrings
test('ignores mutation keywords inside comments and strings', () => {
  assert.strictEqual(
    scanMutations("-- Update [T]\nSelect 'delete from x' as note;").isProvablyReadOnly,
    true
  );
});

// Extra cases mirroring the C# extras
test('Delete From @variable is provably read-only', () => {
  assert.strictEqual(scanMutations('Delete From @TempRows;').isProvablyReadOnly, true);
});

test('Truncate Table is not read-only and records Truncate hit', () => {
  const r = scanMutations('Truncate Table dbo.Logs;');
  assert.strictEqual(r.isProvablyReadOnly, false);
  assert.ok(r.mutations.some((m) => m.kind === 'Truncate'));
});

test('Exec is not read-only and records Exec hit', () => {
  const r = scanMutations('Exec sp_DoSomething;');
  assert.strictEqual(r.isProvablyReadOnly, false);
  assert.ok(r.mutations.some((m) => m.kind === 'Exec'));
});

test('Select Into real table is not read-only and records SelectInto hit', () => {
  const r = scanMutations('Select Id Into dbo.Archive From dbo.Source;');
  assert.strictEqual(r.isProvablyReadOnly, false);
  assert.ok(r.mutations.some((m) => m.kind === 'SelectInto'));
});

test('Select Into @variable is provably read-only', () => {
  assert.strictEqual(
    scanMutations('Select Id Into @Temp From dbo.Source;').isProvablyReadOnly,
    true
  );
});

test('Begin Transaction keyword detected', () => {
  assert.strictEqual(
    scanMutations('Begin Transaction; Select 1; Commit;').hasOwnTransaction,
    true
  );
});

test('no own transaction when absent', () => {
  assert.strictEqual(scanMutations('Select 1;').hasOwnTransaction, false);
});

test('mutation keyword in block comment is ignored', () => {
  assert.strictEqual(
    scanMutations('/* Insert Into RealTable values(1) */ Select 1;').isProvablyReadOnly,
    true
  );
});
