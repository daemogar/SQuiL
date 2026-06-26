import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { shapeHints } from './shapeHints';

test('SP0020 fires when two differently-named tables share an exact signature', () => {
  const hints = shapeHints(parseSQuiL([
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Declare @Returns_Persons table(PersonID int, FullName varchar(100));',
    'Use Db;',
    'Select 1;',
  ].join('\n')));

  assert.strictEqual(hints.length, 2, 'one hint per participating declaration');
  assert.strictEqual(hints[0].code, 'SP0020');
  assert.ok(hints[0].message.includes('Persons') || hints[0].message.includes('Person'));
});

test('SP0020 silent when signatures differ', () => {
  const hints = shapeHints(parseSQuiL([
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Declare @Returns_Pet table(PetID int, Name varchar(50));',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.strictEqual(hints.length, 0);
});