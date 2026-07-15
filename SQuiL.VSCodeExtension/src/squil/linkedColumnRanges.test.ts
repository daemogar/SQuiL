import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { linkedColumnRanges } from './linkedColumnRanges';

test('linkedColumnRanges tags both the parent PK column and the matching child FK column', () => {
  const parsed = parseSQuiL([
    '--Name: ParentChild',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const ranges = linkedColumnRanges(parsed);
  assert.strictEqual(ranges.length, 2, 'exactly the PK column and the FK column should be tagged');

  // Parent.ParentID — line 1 (0-based), "Declare @Returns_Parent table(ParentID ..."
  const parentRange = ranges.find(r => r.line === 1);
  assert.ok(parentRange, 'parent PK column should be tagged on its declaration line');
  assert.strictEqual(parentRange!.length, 'ParentID'.length);

  // Child.ParentID — line 2
  const childRange = ranges.find(r => r.line === 2);
  assert.ok(childRange, 'child FK-by-convention column should be tagged on its declaration line');
  assert.strictEqual(childRange!.length, 'ParentID'.length);
});

test('linkedColumnRanges does not tag a plain (non-key) column or an unrelated PK', () => {
  const parsed = parseSQuiL([
    '--Name: ParentChildPlusUnrelated',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Declare @Returns_Unrelated table(UnrelatedID int Primary Key, X int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const ranges = linkedColumnRanges(parsed);
  // Still exactly 2 — Unrelated's PK plays no link role, and Name/X are plain columns.
  assert.strictEqual(ranges.length, 2);
  assert.ok(!ranges.some(r => r.line === 3), 'the unrelated table\'s Primary Key must not be tagged');
});

test('linkedColumnRanges is empty on a fully-flat file (no links anywhere)', () => {
  const parsed = parseSQuiL([
    '--Name: FlatFile',
    'Declare @Returns_Person table(PersonID int Primary Key, Name varchar(50));',
    'Declare @Returns_Pet table(PetID int Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(linkedColumnRanges(parsed).length, 0);
});

test('linkedColumnRanges covers the INPUT graph independently of the OUTPUT graph', () => {
  const parsed = parseSQuiL([
    '--Name: MixedIsolation',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Declare @Param_Solo table(SoloID int Primary Key, X int);',
    'Use [Db];',
    'Insert Into dbo.S Select SoloID, X From @Param_Solo;',
  ].join('\n'));

  const ranges = linkedColumnRanges(parsed);
  // Only the OUTPUT link (Parent/Child) is tagged; Solo is an isolated input table.
  assert.strictEqual(ranges.length, 2);
  assert.ok(!ranges.some(r => r.line === 3), 'an isolated input table\'s PK must not be tagged');
});

test('linkedColumnRanges tags an INPUT link the same way as an OUTPUT link', () => {
  const parsed = parseSQuiL([
    '--Name: InputLink',
    'Declare @Param_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Params_Child table(ChildID int, ParentID int);',
    'Use [Db];',
    'Insert Into dbo.P Select ParentID, Name From @Param_Parent;',
    'Insert Into dbo.C Select ChildID, ParentID From @Params_Child;',
  ].join('\n'));

  const ranges = linkedColumnRanges(parsed);
  assert.strictEqual(ranges.length, 2);
  assert.ok(ranges.some(r => r.line === 1));
  assert.ok(ranges.some(r => r.line === 2));
});
