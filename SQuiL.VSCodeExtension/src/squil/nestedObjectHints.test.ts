import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { nestedObjectHints } from './nestedObjectHints';

// SP0035: orphaned Primary Key — only surfaced when nesting is already in play
// elsewhere in the file (at least one real parent/child link exists).

test('SP0035 fires on a Primary Key no child links to, when a link exists elsewhere', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: OrphanWithLink',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    // Child is a leaf with no PK of its own — only Parent's PK is in play here.
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    // Unrelated's PK is a genuine orphan: nesting is in play (Parent/Child link
    // above) but nothing carries an UnrelatedID column.
    'Declare @Returns_Unrelated table(UnrelatedID int Primary Key, X int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n')));

  assert.strictEqual(hints.length, 1, 'only the truly orphaned PK should be flagged');
  assert.strictEqual(hints[0].code, 'SP0035');
  assert.ok(hints[0].message.includes('UnrelatedID'), 'message should name the orphaned PK column');
  assert.ok(hints[0].message.includes('Unrelated'), 'message should name the owning table');
});

test('SP0035 stays silent on a fully-flat file with unrelated Primary Keys (no links anywhere)', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: FlatFile',
    'Declare @Returns_Person table(PersonID int Primary Key, Name varchar(50));',
    'Declare @Returns_Pet table(PetID int Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n')));

  assert.strictEqual(hints.length, 0, 'no nesting is in play, so unrelated PKs must not be nagged');
});

test('SP0035 stays silent when every declared Primary Key has a linking child', () => {
  // Child is a leaf with no PK of its own, so Parent's PK is the only PK in
  // play and it IS linked — no orphan.
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: FullyLinked',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n')));

  assert.strictEqual(hints.length, 0);
});
