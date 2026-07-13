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
