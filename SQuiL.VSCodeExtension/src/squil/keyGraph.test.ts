import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { buildKeyGraph } from './keyGraph';

// R1: edges orient by declaration order, not by which side owns the Primary Key.
// `parent` is always the earlier-declared block (the container); `isEmbed` records whether the
// later-declared block (`child`) owns the shared key column.

test('orients by declaration order and flags an embed', () => {
  const parsed = parseSQuiL([
    '--Name: EmbedDirection',
    'Declare @Returns_Structure table(Title varchar(50), ContactID varchar(10));',
    'Declare @Returns_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const { edges } = buildKeyGraph(parsed.variables);
  assert.strictEqual(edges.length, 1);
  assert.strictEqual(edges[0].parent.name, 'Structure');
  assert.strictEqual(edges[0].child.name, 'Contact');
  assert.strictEqual(edges[0].isEmbed, true);
});

test('keeps classic child orientation when the Primary-Key owner is declared first', () => {
  const parsed = parseSQuiL([
    '--Name: ChildDirection',
    'Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);',
    'Declare @Returns_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const { edges } = buildKeyGraph(parsed.variables);
  assert.strictEqual(edges.length, 1);
  assert.strictEqual(edges[0].parent.name, 'Transcript');
  assert.strictEqual(edges[0].child.name, 'Institution');
  assert.strictEqual(edges[0].isEmbed, false);
});

// C1 regression (Task 1 review): A and B are linked by TWO reciprocal key columns (A carries
// B's Primary Key "BID"; B carries A's Primary Key "AID"). Dedupe must be keyed on the pair
// alone, not (pair, key) — otherwise this produces two edges with the same parent/child, which
// the generator's mirror turned into a duplicate emitted member (CS0102).
test('two reciprocal key columns between the same pair still produce exactly one edge', () => {
  const parsed = parseSQuiL([
    '--Name: ReciprocalKeys',
    'Declare @Return_A table(AID int Primary Key, BID int);',
    'Declare @Return_B table(BID int Primary Key, AID int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const { edges } = buildKeyGraph(parsed.variables);
  assert.strictEqual(edges.length, 1);
  assert.strictEqual(edges[0].parent.name, 'A');
  assert.strictEqual(edges[0].child.name, 'B');
  assert.strictEqual(edges[0].keyName, 'BID');
  assert.strictEqual(edges[0].isEmbed, true);
});
