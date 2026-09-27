import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { describeColumnLinkRole } from './linkRoleHints';

// Transcript → Institution → Course fixture (same shape as previewGenerator's
// nested-objects tests): Transcript's PK is TranscriptID; Institution links
// to it (FK-by-convention) and declares its own PK InstitutionID; Course
// links to Institution but has no PK of its own (leaf).

const SQL = [
  '--Name: NestedHover',
  'Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);',
  'Declare @Returns_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));',
  'Declare @Returns_Course table(CourseID int, InstitutionID int, Title varchar(50));',
  'Use [Db];',
  'Select 1;',
].join('\n');

const lines = SQL.split('\n');

// Column position lookup: (line, character) of the Nth occurrence (0-based)
// of `columnName` on `lineIdx` — must match the parser's own (line,
// character) convention (start of the column NAME token).
function colPos(lineIdx: number, columnName: string, occurrence = 0): { line: number; character: number } {
  let character = -1;
  let from = 0;
  for (let i = 0; i <= occurrence; i++) {
    character = lines[lineIdx].indexOf(columnName, from);
    assert.ok(character >= 0, `fixture line ${lineIdx} should contain occurrence ${i} of ${columnName}`);
    from = character + 1;
  }
  return { line: lineIdx, character };
}

test('hovering the Primary Key column (with a linking child) explains the PK role', () => {
  const parsed = parseSQuiL(SQL);
  const { line, character } = colPos(1, 'TranscriptID'); // Transcript's own PK

  const text = describeColumnLinkRole(parsed, line, character);

  assert.ok(text, 'expected PK role text');
  assert.ok(text!.includes('Primary Key'));
  assert.ok(text!.includes('TranscriptID'));
  assert.ok(text!.includes('Transcript'));
});

test('hovering a foreign-key-by-convention column explains the FK role', () => {
  const parsed = parseSQuiL(SQL);
  // Institution's TranscriptID column (not its own PK) matches Transcript's
  // PK by convention.
  const { line, character } = colPos(2, 'TranscriptID');

  const text = describeColumnLinkRole(parsed, line, character);

  assert.ok(text, 'expected FK role text');
  assert.ok(text!.includes('Foreign key by convention'));
  assert.ok(text!.includes('Institution'));
  assert.ok(text!.includes('Transcript'));
});

test('hovering a non-link column (SchoolName) leaves hover unchanged (undefined)', () => {
  const parsed = parseSQuiL(SQL);
  const { line, character } = colPos(2, 'SchoolName');

  const text = describeColumnLinkRole(parsed, line, character);

  assert.strictEqual(text, undefined);
});

test('hovering a Primary Key column with no linking child notes the orphan (SP0035 spirit)', () => {
  const sql = [
    '--Name: OrphanHover',
    'Declare @Returns_Unrelated table(UnrelatedID int Primary Key, X int);',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const parsed = parseSQuiL(sql);
  const character = sql.split('\n')[1].indexOf('UnrelatedID');

  const text = describeColumnLinkRole(parsed, 1, character);

  assert.ok(text, 'expected an orphan PK note');
  assert.ok(text!.includes('Primary Key'));
  assert.ok(text!.includes('UnrelatedID'));
});

test('hovering a Primary Key column in a file with NO links at all returns undefined (graceful degradation)', () => {
  // Single output table, zero edges (hasLinks === false). The orphan-PK note
  // must NOT fire here — that's the case the "with no linking child" test
  // above misses, since its fixture has links elsewhere (Parent/Child).
  const sql = [
    '--Name: NoLinksHover',
    'Declare @Returns_Foo table(FooID int Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const parsed = parseSQuiL(sql);
  const character = sql.split('\n')[1].indexOf('FooID');

  const text = describeColumnLinkRole(parsed, 1, character);

  assert.strictEqual(text, undefined);
});

test('non-column position (e.g. the Use statement) returns undefined', () => {
  const parsed = parseSQuiL(SQL);
  const text = describeColumnLinkRole(parsed, 4, 0); // 'Use [Db];' line
  assert.strictEqual(text, undefined);
});

// ── INPUT (`@Param_`/`@Params_`) side — same hover roles, independent graph
// (Task 15). Fixture mirrors the OUTPUT one above: Order(PK)→Line(FK, leaf).

const INPUT_SQL = [
  '--Name: NestedInputHover',
  'Declare @Param_Order table(OrderID int Primary Key, CustomerName varchar(50));',
  'Declare @Params_Line table(LineID int, OrderID int, Amount decimal(18,2));',
  'Use [Db];',
  'Insert Into dbo.Orders Select OrderID, CustomerName From @Param_Order;',
  'Insert Into dbo.Lines Select LineID, OrderID, Amount From @Params_Line;',
].join('\n');

const inputLines = INPUT_SQL.split('\n');

function inputColPos(lineIdx: number, columnName: string): { line: number; character: number } {
  const character = inputLines[lineIdx].indexOf(columnName);
  assert.ok(character >= 0, `fixture line ${lineIdx} should contain ${columnName}`);
  return { line: lineIdx, character };
}

test('hovering an INPUT Primary Key column (with a linking child) explains the PK role', () => {
  const parsed = parseSQuiL(INPUT_SQL);
  const { line, character } = inputColPos(1, 'OrderID'); // Order's own PK

  const text = describeColumnLinkRole(parsed, line, character);

  assert.ok(text, 'expected PK role text');
  assert.ok(text!.includes('Primary Key'));
  assert.ok(text!.includes('OrderID'));
  assert.ok(text!.includes('Order'));
});

test('hovering an INPUT foreign-key-by-convention column explains the FK role', () => {
  const parsed = parseSQuiL(INPUT_SQL);
  const { line, character } = inputColPos(2, 'OrderID'); // Line's FK-by-convention column

  const text = describeColumnLinkRole(parsed, line, character);

  assert.ok(text, 'expected FK role text');
  assert.ok(text!.includes('Foreign key by convention'));
  assert.ok(text!.includes('Line'));
  assert.ok(text!.includes('Order'));
});

test('hovering a non-link INPUT column (CustomerName) leaves hover unchanged (undefined)', () => {
  const parsed = parseSQuiL(INPUT_SQL);
  const { line, character } = inputColPos(1, 'CustomerName');

  const text = describeColumnLinkRole(parsed, line, character);

  assert.strictEqual(text, undefined);
});

test('hovering an INPUT Primary Key column in a file with NO input links at all returns undefined (graceful degradation)', () => {
  const sql = [
    '--Name: NoInputLinksHover',
    'Declare @Params_Foo table(FooID int Primary Key, Name varchar(50));',
    'Use [Db];',
    'Insert Into dbo.Foos Select FooID, Name From @Params_Foo;',
  ].join('\n');
  const parsed = parseSQuiL(sql);
  const character = sql.split('\n')[1].indexOf('FooID');

  const text = describeColumnLinkRole(parsed, 1, character);

  assert.strictEqual(text, undefined);
});

// ── Containment direction: the PK owner may be the NESTED side (embed). ──

function roleAt(sql: string, lineIdx: number, columnName: string): string | undefined {
  const character = sql.split('\n')[lineIdx].indexOf(columnName);
  assert.ok(character >= 0, `fixture line ${lineIdx} should contain ${columnName}`);
  return describeColumnLinkRole(parseSQuiL(sql), lineIdx, character);
}

const EMBED_SQL = [
  '--Name: EmbedHover',
  'Declare @Returns_Structure table(Title varchar(50), ContactID varchar(10));',
  'Declare @Returns_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));',
  'Use [Db];',
  'Select 1;',
].join('\n');

test('embed: hovering the lookup\'s own Primary Key says it embeds into its container', () => {
  const text = roleAt(EMBED_SQL, 2, 'ContactID');
  assert.ok(text, 'expected PK role text');
  assert.ok(text!.includes('Primary Key'));
  assert.ok(!text!.includes('no child table links'), text);
  assert.ok(text!.includes('Structure') && text!.includes('single'), text);
});

test('embed: hovering the container\'s key column explains the embed', () => {
  const text = roleAt(EMBED_SQL, 1, 'ContactID');
  assert.ok(text, 'expected FK role text');
  assert.ok(text!.includes('Foreign key by convention'));
  assert.ok(text!.includes('Contact') && text!.includes('Structure') && text!.includes('single'), text);
});

const JUNCTION_SQL = [
  '--Name: JunctionHover',
  'Declare @Returns_Student table(StudentID int Primary Key, Name varchar(50));',
  'Declare @Returns_Course table(CourseID int Primary Key, Title varchar(50));',
  'Declare @Returns_Enrollment table(StudentID int, CourseID int, Grade varchar(2));',
  'Use [Db];',
  'Select 1;',
].join('\n');

test('junction: the embedded lookup\'s PK (Course.CourseID) is linked, not an orphan', () => {
  const text = roleAt(JUNCTION_SQL, 2, 'CourseID');
  assert.ok(text && text.includes('Primary Key') && !text.includes('no child table links'), text);
  assert.ok(text!.includes('Enrollment'), text);
});

test('junction: Enrollment.CourseID explains the embed; Enrollment.StudentID keeps the classic child text', () => {
  const course = roleAt(JUNCTION_SQL, 3, 'CourseID');
  assert.ok(course && course.includes('Foreign key by convention') && course.includes('Course'), course);
  const student = roleAt(JUNCTION_SQL, 3, 'StudentID');
  assert.ok(student && student.includes('nest under `Student`'), student);
});

test('classic, child declared first: the later PK owner embeds into the earlier block', () => {
  const sql = [
    '--Name: ChildFirstHover',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const pk = roleAt(sql, 2, 'ParentID');
  assert.ok(pk && pk.includes('Primary Key') && !pk.includes('no child table links') && pk.includes('Child'), pk);
  const fk = roleAt(sql, 1, 'ParentID');
  assert.ok(fk && fk.includes('Foreign key by convention') && fk.includes('Parent'), fk);
});

test('hovering an OUTPUT column is unaffected by an unrelated INPUT-side link, and vice versa (graphs stay independent)', () => {
  const sql = [
    '--Name: MixedHover',
    'Declare @Return_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Declare @Params_Solo table(SoloID int Primary Key, X int);',
    'Use [Db];',
    'Insert Into dbo.S Select SoloID, X From @Params_Solo;',
  ].join('\n');
  const parsed = parseSQuiL(sql);
  const lines2 = sql.split('\n');

  // Output side: Parent's PK legitimately has a linking child (Child) — PK-with-child text.
  const parentPos = lines2[1].indexOf('ParentID');
  const outputText = describeColumnLinkRole(parsed, 1, parentPos);
  assert.ok(outputText?.includes('Primary Key') && outputText.includes('child tables'));

  // Input side: Solo's PK has NO input-side link — must be undefined, not
  // "rescued" into a hit by the unrelated output-side link existing.
  const soloPos = lines2[2].indexOf('SoloID');
  const inputText = describeColumnLinkRole(parsed, 2, soloPos);
  assert.strictEqual(inputText, undefined);
});
