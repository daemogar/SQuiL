import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { shapeHints } from './shapeHints';

test('SP0020 fires when two differently-named tables share an exact signature (cross-side)', () => {
  // Same canonical shape but on opposite sides (input vs output) — SP0030 only
  // fires on same-side output pairs, so this cross-side pair is SP0020's domain.
  const hints = shapeHints(parseSQuiL([
    'Declare @Params_Employee table(PersonID int, FullName varchar(100));',
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Use Db;',
    'Select 1;',
  ].join('\n')));

  assert.strictEqual(hints.length, 2, 'one hint per participating declaration');
  assert.strictEqual(hints[0].code, 'SP0020');
  assert.ok(hints[0].message.includes('Employee') || hints[0].message.includes('Person'));
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

// SP0020: different-name pair that is identical EXCEPT for a column size SHOULD
// fire — sizes may differ per spec, so they are "same shape" from the generator's
// perspective.  This is a RED→GREEN regression guard added by the size-strip fix.
// Uses cross-side (input vs output) so SP0030 does not suppress the SP0020 hint.
test('SP0020 fires when differently-named tables share shape except column size (cross-side)', () => {
  const hints = shapeHints(parseSQuiL([
    'Declare @Params_Employee table(PersonID int, Name varchar(100));',
    'Declare @Returns_People table(PersonID int, Name varchar(50));',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.ok(hints.length >= 2, 'SP0020 must fire for each participating declaration');
  assert.ok(hints.every(h => h.code === 'SP0020'));
  assert.ok(
    hints.some(h => h.message.includes('People') || h.message.includes('Employee')),
    'hint must name a counterpart',
  );
});

// ── SP0017 / SP0020 firewall ─────────────────────────────────────────────────
// SP0017 (same-name + different-shape) and SP0020 (different-name + same-shape)
// are complements and must never overlap.  These guard that boundary.

test('SP0020 silent for a same-name pair with an identical signature (SP0017 domain)', () => {
  // Two @Returns_Person declarations with the SAME shape are a legitimate merge,
  // NOT a similar-signature accident — SP0020 must stay silent.
  const hints = shapeHints(parseSQuiL([
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Use Db;',
    'Select 1;',
  ].join('\n')));
  assert.strictEqual(hints.length, 0);
});

test('SP0020 fires only across differently-named vars in a mixed same/different group (cross-side)', () => {
  // @Params_Person declared twice (same shape) + @Returns_Persons (same shape).
  // SP0020 must point Person↔Persons but NEVER Person↔Person.
  // Using input @Params_ for Person so SP0030 (output-only) does not suppress Person hints.
  const hints = shapeHints(parseSQuiL([
    'Declare @Params_Person table(PersonID int, FullName varchar(100));',
    'Declare @Params_Person table(PersonID int, FullName varchar(100));',
    'Declare @Returns_Persons table(PersonID int, FullName varchar(100));',
    'Use Db;',
    'Select 1;',
  ].join('\n')));

  // Every hint is SP0020 and every hint names a DIFFERENT counterpart than its
  // own subject — no Person↔Person self/same-name pairing.
  assert.ok(hints.length > 0, 'at least one cross-name hint expected');
  assert.ok(hints.every(h => h.code === 'SP0020'));
  // No hint should be "Person ... same column signature as ... Person".
  assert.ok(
    !hints.some(h => /`Person` has the same column signature as `Person`/.test(h.message)),
    'no Person↔Person (same-name) hint may be emitted',
  );
  // The cross-name pairing must be present.
  assert.ok(
    hints.some(h => h.message.includes('Persons')),
    'a Person↔Persons cross-name hint must be present',
  );
});
