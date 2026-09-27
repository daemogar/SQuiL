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

  // The Parent/Child link also produces one SP0045 containment hint (Task 6) —
  // filter to SP0035 to keep this assertion about the orphan hint alone.
  const sp0035 = hints.filter(h => h.code === 'SP0035');
  assert.strictEqual(sp0035.length, 1, 'only the truly orphaned PK should be flagged');
  assert.ok(sp0035[0].message.includes('UnrelatedID'), 'message should name the orphaned PK column');
  assert.ok(sp0035[0].message.includes('Unrelated'), 'message should name the owning table');
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
  // play and it IS linked — no orphan. (The link itself still produces one
  // SP0045 containment hint — Task 6 — so filter to SP0035 here.)
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: FullyLinked',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n')));

  assert.strictEqual(hints.filter(h => h.code === 'SP0035').length, 0);
});

// ── SP0035 on the INPUT (`@Param_`/`@Params_`) key graph — same hint,
// independent graph (Task 15) ────────────────────────────────────────────

test('SP0035 fires on an orphaned INPUT Primary Key, when an input link exists elsewhere', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: OrphanWithLinkInput',
    'Declare @Param_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Params_Child table(ChildID int, ParentID int);',
    'Declare @Params_Unrelated table(UnrelatedID int Primary Key, X int);',
    'Use [Db];',
    'Insert Into dbo.P Select ParentID, Name From @Param_Parent;',
    'Insert Into dbo.C Select ChildID, ParentID From @Params_Child;',
    'Insert Into dbo.U Select UnrelatedID, X From @Params_Unrelated;',
  ].join('\n')));

  // The Parent/Child link also produces one SP0045 containment hint (Task 6) —
  // filter to SP0035 to keep this assertion about the orphan hint alone.
  const sp0035 = hints.filter(h => h.code === 'SP0035');
  assert.strictEqual(sp0035.length, 1, 'only the truly orphaned input PK should be flagged');
  assert.ok(sp0035[0].message.includes('UnrelatedID'), 'message should name the orphaned PK column');
  assert.ok(sp0035[0].message.includes('Unrelated'), 'message should name the owning table');
});

test('SP0035 stays silent on a fully-flat INPUT file with unrelated Primary Keys (no input links anywhere)', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: FlatInputFile',
    'Declare @Params_Alpha table(AlphaID int Primary Key, N int);',
    'Declare @Params_Beta table(BetaID int Primary Key, M int);',
    'Use [Db];',
    'Insert Into dbo.A Select AlphaID, N From @Params_Alpha;',
    'Insert Into dbo.B Select BetaID, M From @Params_Beta;',
  ].join('\n')));

  assert.strictEqual(hints.length, 0, 'no input nesting is in play, so unrelated PKs must not be nagged');
});

test('SP0035 on the INPUT graph is unaffected by an unrelated OUTPUT-side link (graphs stay independent)', () => {
  // Output side has a real link (Parent/Child); input side has ONE isolated
  // table with its own PK that nothing else links to. The output link must
  // not "activate" hasLinks for the input graph. (The output link still
  // produces one SP0045 containment hint of its own — Task 6 — so filter to
  // SP0035 to keep this assertion about the orphan hint alone.)
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: MixedIsolation',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Declare @Params_Solo table(SoloID int Primary Key, X int);',
    'Use [Db];',
    'Insert Into dbo.S Select SoloID, X From @Params_Solo;',
  ].join('\n')));

  assert.strictEqual(
    hints.filter(h => h.code === 'SP0035').length, 0,
    'input graph has no links of its own, so its orphan PK must stay silent',
  );
});

// ── SP0045: containment-direction hint (Task 6) ─────────────────────────
//
// Editor-only Hint (VS Code) / Info (C#) — NOT a build/generator diagnostic.
// One hint per key-graph edge, anchored on the NESTED (child) variable's
// declaration, explaining why the edge nests the way it does: declaration
// order (R1) for a normal edge, or the container's own reference to the
// nested variable's Primary Key for an R3-inverted junction edge.

test('SP0045 explains an embed edge — nests as a single object because the container is declared first', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: EmbedDirection',
    'Declare @Returns_Structure table(Title varchar(50), ContactID varchar(10));',
    'Declare @Returns_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n')));

  const sp0045 = hints.filter(h => h.code === 'SP0045');
  assert.strictEqual(sp0045.length, 1);
  assert.ok(sp0045[0].message.includes('`Contact` nests inside `Structure`'));
  assert.ok(sp0045[0].message.includes('as a single object'));
  assert.ok(sp0045[0].message.includes('declared first'));
  assert.ok(sp0045[0].message.includes('Reorder the declarations'));
});

test('SP0045 explains a classic list child', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: ChildDirectionList',
    'Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);',
    'Declare @Returns_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n')));

  const sp0045 = hints.filter(h => h.code === 'SP0045');
  assert.strictEqual(sp0045.length, 1);
  assert.ok(sp0045[0].message.includes('`Institution` nests inside `Transcript`'));
  assert.ok(sp0045[0].message.includes('as a list'));
  assert.ok(sp0045[0].message.includes('declared first'));
});

test('SP0045 explains a classic single-object child', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: ChildDirectionObject',
    'Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);',
    'Declare @Return_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n')));

  const sp0045 = hints.filter(h => h.code === 'SP0045');
  assert.strictEqual(sp0045.length, 1);
  assert.ok(sp0045[0].message.includes('`Institution` nests inside `Transcript`'));
  assert.ok(sp0045[0].message.includes('as a single object'));
  assert.ok(sp0045[0].message.includes('declared first'));
});

test('SP0045 on an R3-inverted junction edge explains the lookup, not declaration order', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: Junction',
    'Declare @Returns_Student table(StudentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Course table(CourseID int Primary Key, Title varchar(50));',
    'Declare @Returns_Enrollment table(StudentID int, CourseID int, Grade varchar(2));',
    'Use [Db];',
    'Select * From @Returns_Student;',
    'Select * From @Returns_Course;',
    'Select * From @Returns_Enrollment;',
  ].join('\n')));

  const sp0045 = hints.filter(h => h.code === 'SP0045');
  assert.strictEqual(sp0045.length, 2);

  // Student -> Enrollment: a normal, non-inverted list-child edge.
  const toEnrollment = sp0045.find(h => h.message.includes('`Enrollment` nests inside `Student`'))!;
  assert.ok(toEnrollment, 'Student -> Enrollment hint should exist');
  assert.ok(toEnrollment.message.includes('as a list'));
  assert.ok(toEnrollment.message.includes('declared first'));

  // Enrollment -> Course: the R3-inverted edge. Enrollment is declared AFTER
  // Course, so the message must NOT claim declaration order.
  const toCourse = sp0045.find(h => h.message.includes('`Course` nests inside `Enrollment`'))!;
  assert.ok(toCourse, 'Enrollment -> Course hint should exist');
  assert.ok(toCourse.message.includes('as a single object'));
  assert.ok(!toCourse.message.includes('declared first'), 'inverted edge must not claim declaration order');
  assert.ok(toCourse.message.includes('references its Primary Key'));
  assert.ok(toCourse.message.includes('`CourseID`'));
  assert.ok(!toCourse.message.includes('Reorder the declarations'));
});

test('SP0045 stays silent on a flat file with no links', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: FlatFile2',
    'Declare @Returns_Person table(PersonID int Primary Key, Name varchar(50));',
    'Declare @Returns_Pet table(PetID int Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n')));

  assert.strictEqual(hints.filter(h => h.code === 'SP0045').length, 0);
});

test('SP0045 applies independently to the INPUT graph too', () => {
  const hints = nestedObjectHints(parseSQuiL([
    '--Name: InputEmbed',
    'Declare @Params_Structure table(Title varchar(50), ContactID varchar(10));',
    'Declare @Params_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));',
    'Use [Db];',
    'Insert Into dbo.S Select Title, ContactID From @Params_Structure;',
    'Insert Into dbo.C Select ContactID, Name From @Params_Contact;',
  ].join('\n')));

  const sp0045 = hints.filter(h => h.code === 'SP0045');
  assert.strictEqual(sp0045.length, 1);
  assert.ok(sp0045[0].message.includes('`Contact` nests inside `Structure`'));
  assert.ok(sp0045[0].message.includes('as a single object'));
});
