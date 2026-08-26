import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { buildKeyGraph, KeyGraphEdge, OUTPUT_TABLE_ROLES } from './keyGraph';
import { SQuiLVariable } from './parser';

// `KeyGraphResult` (the linter-facing shape) carries no `roots` field — this test-only helper
// derives it the same way the generator/preview do: candidates (OUTPUT table/object variables
// with columns) that are nobody's edge child.
function rootsOf(variables: SQuiLVariable[], edges: KeyGraphEdge[]): SQuiLVariable[] {
  const candidates = variables.filter(v => OUTPUT_TABLE_ROLES.has(v.role) && Array.isArray(v.columns) && v.columns.length > 0);
  const children = new Set(edges.map(e => e.child));
  return candidates.filter(v => !children.has(v));
}

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

// ── Task 3: R3 multi-container resolution ──────────────────────────────────
// Mirrors SQuiL.Tests/NestedObjects/KeyGraphTests.cs — change one side, change the other.

test('a junction keeps its earliest container and inverts the rest', () => {
  const parsed = parseSQuiL([
    '--Name: Junction',
    'Declare @Returns_Student table(StudentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Course table(CourseID int Primary Key, Title varchar(50));',
    'Declare @Returns_Enrollment table(StudentID int, CourseID int, Grade varchar(2));',
    'Use [Db];',
    'Select * From @Returns_Student;',
    'Select * From @Returns_Course;',
    'Select * From @Returns_Enrollment;',
  ].join('\n'));

  const { edges } = buildKeyGraph(parsed.variables);
  const roots = rootsOf(parsed.variables, edges);
  assert.strictEqual(edges.length, 2);

  const kept = edges.find(e => e.child.name === 'Enrollment')!;
  assert.ok(kept, 'Enrollment edge should exist');
  assert.strictEqual(kept.parent.name, 'Student');
  assert.strictEqual(kept.isEmbed, false);

  const inverted = edges.find(e => e.child.name === 'Course')!;
  assert.ok(inverted, 'Course edge should exist');
  assert.strictEqual(inverted.parent.name, 'Enrollment');
  assert.strictEqual(inverted.isEmbed, true);

  assert.strictEqual(roots.length, 1);
  assert.strictEqual(roots[0].name, 'Student');
});

test('a shared lookup allows one Primary-Key owner in many containers', () => {
  const parsed = parseSQuiL([
    '--Name: SharedLookup',
    'Declare @Returns_Structure table(Title varchar(50), ContactID varchar(10));',
    'Declare @Returns_Widget table(Label varchar(50), ContactID varchar(10));',
    'Declare @Returns_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select * From @Returns_Structure;',
    'Select * From @Returns_Widget;',
    'Select * From @Returns_Contact;',
  ].join('\n'));

  const { edges } = buildKeyGraph(parsed.variables);
  const roots = rootsOf(parsed.variables, edges);
  assert.strictEqual(edges.length, 2);
  assert.ok(edges.every(e => e.isEmbed));
  assert.ok(edges.every(e => e.child.name === 'Contact'));
  assert.deepStrictEqual(roots.map(r => r.name), ['Structure', 'Widget']);
});

// A single R3 pass is NOT enough here — see the matching C# test
// (`MultiContainerResolutionIteratesToAFixedPoint`) for the full pass-by-pass trace. A naive
// single-pass implementation would stop after resolving C's conflict and report 3 edges with B
// double-parented; the fixed-point loop converges on exactly 2.
test('multi-container resolution iterates to a fixed point', () => {
  const parsed = parseSQuiL([
    '--Name: FixedPoint',
    'Declare @Returns_A table(AID int Primary Key, CID int);',
    'Declare @Returns_B table(BID int Primary Key, AID int);',
    'Declare @Returns_C table(CID int Primary Key, BID int);',
    'Use [Db];',
    'Select * From @Returns_A;',
    'Select * From @Returns_B;',
    'Select * From @Returns_C;',
  ].join('\n'));

  const { edges, errors } = buildKeyGraph(parsed.variables);
  const roots = rootsOf(parsed.variables, edges);
  assert.strictEqual(errors.length, 0);
  assert.strictEqual(edges.length, 2);

  const toB = edges.find(e => e.child.name === 'B')!;
  assert.ok(toB);
  assert.strictEqual(toB.parent.name, 'A');
  assert.strictEqual(toB.keyName, 'AID');
  assert.strictEqual(toB.isEmbed, false);

  const toC = edges.find(e => e.child.name === 'C')!;
  assert.ok(toC);
  assert.strictEqual(toC.parent.name, 'A');
  assert.strictEqual(toC.keyName, 'CID');
  assert.strictEqual(toC.isEmbed, true);

  assert.strictEqual(roots.length, 1);
  assert.strictEqual(roots[0].name, 'A');
});

// The minimal reachable cycle — see the matching C# test
// (`MultiContainerResolutionCanCascadeIntoACycle`) for the full pass-by-pass trace. Exhaustive
// search over every 3-block pair/owner/key-sharing configuration found none that cycle; this
// 4-block junction+shared-lookup combination is the smallest that does.
test('multi-container resolution can cascade into a cycle', () => {
  const parsed = parseSQuiL([
    '--Name: Cascade',
    'Declare @Returns_Summary table(ProductID varchar(10));',
    'Declare @Returns_Category table(CategoryID int Primary Key, Name varchar(50));',
    'Declare @Returns_Product table(ProductID varchar(10) Primary Key, CategoryID int, Title varchar(50));',
    'Declare @Returns_Junction table(CategoryID int, ProductID varchar(10), Note varchar(50));',
    'Use [Db];',
    'Select * From @Returns_Summary;',
    'Select * From @Returns_Category;',
    'Select * From @Returns_Product;',
    'Select * From @Returns_Junction;',
  ].join('\n'));

  const { errors } = buildKeyGraph(parsed.variables);
  const cycles = errors.filter(f => f.kind === 'cycle');
  assert.strictEqual(cycles.length, 1);

  const participants = [cycles[0].variable.name, cycles[0].otherVariable.name];
  for (const name of participants) {
    assert.ok(['Category', 'Product', 'Junction'].includes(name), `${name} should be one of the rotating containers`);
  }
  assert.ok(!participants.includes('Summary'));
});
