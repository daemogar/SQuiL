import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import { generateCSharpPreview } from './previewGenerator';

// Preview parity with the generator's opt-in special emission:
//   • bool Debug          only when @Debug declared
//   • bool SuppressDebug  only when @SuppressDebug declared (replaces DebugOnly)
//   • <type>? AsOfDate    only when @AsOfDate declared (nullable typed property)
//   • DebugOnly           never emitted
//   • @EnvironmentName    never a Request property

function preview(sql: string): string {
  const parsed = parseSQuiL(sql);
  return generateCSharpPreview(parsed, parsed.queryName ?? 'Query');
}

test('Request preview omits all specials when none are declared (no DebugOnly)', () => {
  const out = preview([
    '--Name: NoSpecials',
    'Declare @Param_Name varchar(100);',
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Select @Return_Count = Count(*) From Users Where Name = @Param_Name;',
    'Select @Return_Count;',
  ].join('\n'));

  assert.ok(!out.includes('DebugOnly'), 'DebugOnly must never be emitted');
  assert.ok(!out.includes('public bool Debug'), 'Debug must be opt-in');
  assert.ok(!out.includes('public bool SuppressDebug'), 'SuppressDebug must be opt-in');
  assert.ok(!out.includes('AsOfDate'), 'AsOfDate must be opt-in');
});

test('Request preview emits opt-in Debug/SuppressDebug + nullable AsOfDate, never DebugOnly', () => {
  const out = preview([
    '--Name: AsOf',
    'Declare @Debug bit = 1;',
    'Declare @SuppressDebug bit = 0;',
    "Declare @AsOfDate date = '2008-10-01';",
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Set @Return_Count = (Select Count(*) From Logs Where CreatedOn <= @AsOfDate);',
    'Select @Return_Count;',
  ].join('\n'));

  assert.ok(!out.includes('DebugOnly'), 'DebugOnly must never be emitted');
  assert.ok(out.includes('public bool Debug { get; set; }'), 'Debug should be emitted when declared');
  assert.ok(out.includes('public bool SuppressDebug { get; set; }'), 'SuppressDebug should be emitted when declared');
  // @AsOfDate date → DateOnly?, nullable typed Request property.
  assert.ok(out.includes('public DateOnly? AsOfDate { get; set; }'), 'AsOfDate should be a nullable typed property');
});

test('@EnvironmentName is never emitted as a Request property', () => {
  const out = preview([
    '--Name: Env',
    'Declare @EnvironmentName varchar(50);',
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Select @Return_Count = 1;',
    'Select @Return_Count;',
  ].join('\n'));

  assert.ok(!out.includes('EnvironmentName'), '@EnvironmentName must not be a Request property');
  assert.ok(!out.includes('DebugOnly'), 'DebugOnly must never be emitted');
});

test('Table record with no defaults is positional', () => {
  const out = preview([
    '--Name: NoDefaults',
    'Declare @Params_Rows table(RowID int, Name varchar(50));',
    'Use MyDatabase;',
    'Select 1;',
  ].join('\n'));

  assert.ok(out.includes('public partial record Rows('), 'should be positional');
  assert.ok(!out.includes('{ get; init; }'), 'no-default table must not use init props');
});

test('Table record with defaults uses hybrid positional ctor + init props', () => {
  const out = preview([
    '--Name: WithDefaults',
    "Declare @Params_Rows table(RowID int, Amount decimal(18,2) default 1.5, Qty int, Note varchar(50) default 'hello');",
    'Use MyDatabase;',
    'Select 1;',
  ].join('\n'));

  assert.ok(out.includes('public partial record Rows('), 'has positional ctor');
  assert.ok(out.includes('public decimal Amount { get; init; } = 1.5m;'), 'Amount is an init prop with default (non-nullable: no explicit NULL)');
  assert.ok(out.includes('public string Note { get; init; } = "hello";'), 'Note is an init prop with default (non-nullable: no explicit NULL)');

  assert.ok(!/Rows\([^)]*Amount/.test(out), 'Amount must not be a ctor param');
});

test('nullable value-type column with a default renders as nullable init prop', () => {
  const out = preview([
    '--Name: NullableDefault',
    'Declare @Params_Rows table(RowID int, Score int null default 0);',
    'Use MyDatabase;',
    'Select 1;',
  ].join('\n'));

  assert.ok(out.includes('public int? Score { get; init; } = 0;'), 'nullable value-type default stays nullable');
});

test('row records drop the Table/Object suffix and share the bare name', () => {
  const out = preview([
    '--Name: People',
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Declare @Return_Person table(PersonID int, FullName varchar(100));',
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.ok(out.includes('record Person('), 'must emit a bare `Person` record');
  assert.ok(!out.includes('PersonTable'), 'no Table suffix');
  assert.ok(!out.includes('PersonObject'), 'no Object suffix');
  // properties qualify the type with the .Models sub-namespace
  assert.ok(out.includes('List<YourNamespace.Models.Person>?'), 'table-valued use is List<ns.Models.Person>?');
  assert.ok(out.includes('YourNamespace.Models.Person? Person'), 'single-object use is ns.Models.Person?');
});

test('preview emits row records into the .Models sub-namespace', () => {
  const out = preview([
    '--Name: People',
    'Declare @Returns_Person table(PersonID int, FullName varchar(100));',
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  assert.ok(/namespace \w+\.Models;/.test(out), 'records go into <ns>.Models');
  assert.ok(out.includes('record Person('), 'bare record name');
});

test('preview mirrors generator nullability', () => {
  const out = generateCSharpPreview(parseSQuiL([
    'Declare @Params_People table(ID int, FirstName varchar(50) null, LastName varchar(50));',
    'Declare @Param_Name varchar(100);',
    'Declare @Returns_Rows table(N int);',
    'Use Db;', 'Select 1;',
  ].join('\n')), 'Q');

  // Positional ctor: unmarked varchar → non-nullable string, explicit null → string?
  assert.ok(out.includes('string? FirstName'), 'explicit null marker → nullable string?');
  assert.ok(out.includes('string LastName'), 'unmarked varchar → non-nullable string');
  assert.ok(!out.includes('string? LastName'), 'unmarked varchar must NOT be string?');
  // Scalar @Param_Name varchar(100), no null marker → non-nullable string Request property
  assert.ok(out.includes('public string Name { get; set; };'), 'ref type scalar, unmarked → non-nullable');
  // Request input list: List<ns.Models.X>? with = []
  assert.ok(out.includes('public List<YourNamespace.Models.People>? People { get; set; } = [];'), 'list request → List<ns.Models.X>? with = [] initializer');
  // Response output list: List<ns.Models.X>? with NO = []
  assert.ok(out.includes('public List<YourNamespace.Models.Rows>? Rows { get; set; };'), 'list response → List<ns.Models.X>? with no initializer');
  assert.ok(!out.includes('List<YourNamespace.Models.Rows>? Rows { get; set; } = []'), 'response list must not have = [] initializer');
});

// ─── Nested objects (Transcript → Institution → Course) ────────────────────

test('preview renders nested child members and only the root at the Response top level', () => {
  const out = preview([
    '--Name: ThreeLevelListNesting',
    'Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);',
    'Declare @Returns_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));',
    'Declare @Returns_Course table(CourseID int, InstitutionID int, Title varchar(50));',
    'Use Db;',
    'Insert Into @Return_Transcript Select TranscriptID, IssueDate From T;',
    'Insert Into @Returns_Institution Select InstitutionID, TranscriptID, SchoolName From I;',
    'Insert Into @Returns_Course Select CourseID, InstitutionID, Title From C;',
    'Select * From @Return_Transcript;',
    'Select * From @Returns_Institution;',
    'Select * From @Returns_Course;',
  ].join('\n'));

  // Response top level: only the root (Transcript) — Institution/Course must NOT
  // appear as Response properties, they collapse into their parent records.
  assert.ok(out.includes('YourNamespace.Models.Transcript? Transcript { get; set; }'), 'Transcript is the only Response root');
  const responseSection = out.slice(out.indexOf('ThreeLevelListNestingResponse'), out.indexOf('DataContext'));
  assert.ok(!responseSection.includes('Institution'), 'Institution must not be a Response top-level member');
  assert.ok(!responseSection.includes('Course'), 'Course must not be a Response top-level member');

  // Transcript record gets a nested List<...Institution>? member.
  assert.ok(out.includes('public partial record Transcript(int TranscriptID, DateOnly IssueDate)'), 'Transcript record emitted with its own columns');
  assert.ok(out.includes('List<YourNamespace.Models.Institution>? Institution { get; set; }'), 'Transcript record has a nested Institution list member');

  // Institution record gets a nested List<...Course>? member.
  assert.ok(out.includes('List<YourNamespace.Models.Course>? Course { get; set; }'), 'Institution record has a nested Course list member');

  // Course is a leaf: no nested members, still a plain positional record.
  assert.ok(out.includes('public partial record Course(int CourseID, int InstitutionID, string Title);'), 'Course stays a plain positional record (no children)');
});

test('preview renders a nested object child (non-list) as a nullable member, no default', () => {
  const out = preview([
    '--Name: EmbeddedObjectChild',
    'Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);',
    'Declare @Return_Student table(StudentID int Primary Key, TranscriptID int, FirstName varchar(50));',
    'Use Db;',
    'Insert Into @Return_Transcript Select TranscriptID, IssueDate From T;',
    'Insert Into @Return_Student Select StudentID, TranscriptID, FirstName From S;',
    'Select * From @Return_Transcript;',
    'Select * From @Return_Student;',
  ].join('\n'));

  const responseSection = out.slice(out.indexOf('EmbeddedObjectChildResponse'), out.indexOf('DataContext'));
  assert.ok(!responseSection.includes('Student'), 'Student must not be a Response top-level member');
  assert.ok(out.includes('YourNamespace.Models.Student? Student { get; set; }'), 'Transcript record has a nested nullable Student member');
});

test('preview stays flat when Primary Keys exist but no column links them (graceful degradation)', () => {
  const out = preview([
    '--Name: NoLinks',
    'Declare @Returns_A table(AID int Primary Key, Name varchar(50));',
    'Declare @Returns_B table(BID int Primary Key, Name varchar(50));',
    'Use Db;',
    'Select 1;',
  ].join('\n'));

  const responseSection = out.slice(out.indexOf('NoLinksResponse'), out.indexOf('DataContext'));
  assert.ok(responseSection.includes('A { get; set; }'), 'A stays a flat Response member');
  assert.ok(responseSection.includes('B { get; set; }'), 'B stays a flat Response member');
});
