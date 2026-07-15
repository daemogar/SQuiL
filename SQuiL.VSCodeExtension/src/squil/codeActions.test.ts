import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseSQuiL } from './parser';
import {
  tablesMissingPrimaryKey,
  chooseDefaultKeyColumn,
  buildAddPrimaryKeyEdit,
  availableLinkTargets,
  buildInsertLinkColumnEdit,
  isCursorOnVariable,
} from './codeActions';

/** Applies a single-point insertion edit to a source string, for round-trip assertions. */
function applyEdit(source: string, edit: { position: { line: number; character: number }; insertText: string }): string {
  const lines = source.split('\n');
  const line = lines[edit.position.line];
  lines[edit.position.line] = line.slice(0, edit.position.character) + edit.insertText + line.slice(edit.position.character);
  return lines.join('\n');
}

// ── "Add Primary Key" ───────────────────────────────────────────────────────

test('tablesMissingPrimaryKey finds a table with no Primary Key column', () => {
  const parsed = parseSQuiL([
    '--Name: NoPk',
    'Declare @Returns_Child table(ChildID int, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const missing = tablesMissingPrimaryKey(parsed);
  assert.strictEqual(missing.length, 1);
  assert.strictEqual(missing[0].name, 'Child');
});

test('tablesMissingPrimaryKey is silent when every table already declares a Primary Key', () => {
  const parsed = parseSQuiL([
    '--Name: HasPk',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  assert.strictEqual(tablesMissingPrimaryKey(parsed).length, 0);
});

test('chooseDefaultKeyColumn prefers the first *ID column over the first column', () => {
  const parsed = parseSQuiL([
    '--Name: PickId',
    'Declare @Returns_Widget table(Name varchar(50), WidgetID int, Qty int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const widget = tablesMissingPrimaryKey(parsed)[0];
  assert.strictEqual(chooseDefaultKeyColumn(widget).name, 'WidgetID');
});

test('chooseDefaultKeyColumn falls back to the first column when nothing ends in ID', () => {
  const parsed = parseSQuiL([
    '--Name: NoIdColumn',
    'Declare @Returns_Widget table(Name varchar(50), Qty int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const widget = tablesMissingPrimaryKey(parsed)[0];
  assert.strictEqual(chooseDefaultKeyColumn(widget).name, 'Name');
});

test('buildAddPrimaryKeyEdit inserts " Primary Key" right after the chosen column\'s type, and the result re-parses with the PK recognised', () => {
  const source = [
    '--Name: NoPk',
    'Declare @Returns_Child table(ChildID int, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const parsed = parseSQuiL(source);
  const child = tablesMissingPrimaryKey(parsed)[0];

  const edit = buildAddPrimaryKeyEdit(source.split('\n'), child);
  assert.ok(edit);
  assert.match(edit!.title, /Add Primary Key/);
  assert.match(edit!.title, /ChildID/);

  const updated = applyEdit(source, edit!);
  assert.strictEqual(updated.split('\n')[1], 'Declare @Returns_Child table(ChildID int Primary Key, Name varchar(50));');

  const reparsed = parseSQuiL(updated);
  const reChild = reparsed.variables.find(v => v.name === 'Child')!;
  assert.ok(reChild.columns!.find(c => c.name === 'ChildID')!.isPrimaryKey);
  assert.strictEqual(tablesMissingPrimaryKey(reparsed).length, 0, 'the table should no longer be missing a Primary Key');
});

test('buildAddPrimaryKeyEdit works for a multi-line TABLE(...) declaration', () => {
  const source = [
    '--Name: MultiLine',
    'Declare @Returns_Child table(',
    '  ChildID int,',
    '  Name varchar(50)',
    ');',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const parsed = parseSQuiL(source);
  const child = tablesMissingPrimaryKey(parsed)[0];

  const edit = buildAddPrimaryKeyEdit(source.split('\n'), child);
  assert.ok(edit);

  const updated = applyEdit(source, edit!);
  const reparsed = parseSQuiL(updated);
  const reChild = reparsed.variables.find(v => v.name === 'Child')!;
  assert.ok(reChild.columns!.find(c => c.name === 'ChildID')!.isPrimaryKey);
});

// ── "Link to <Table> via <PK>" ───────────────────────────────────────────────

test('availableLinkTargets offers a parent with a Primary Key the child does not already carry', () => {
  const parsed = parseSQuiL([
    '--Name: NeedsLink',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, Note varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const child = parsed.variables.find(v => v.name === 'Child')!;
  const targets = availableLinkTargets(parsed, { ...child, columns: child.columns! } as any);
  assert.strictEqual(targets.length, 1);
  assert.strictEqual(targets[0].parent.name, 'Parent');
  assert.strictEqual(targets[0].pkColumn.name, 'ParentID');
});

test('availableLinkTargets excludes a parent the child already links to', () => {
  const parsed = parseSQuiL([
    '--Name: AlreadyLinked',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, ParentID int);',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const child = parsed.variables.find(v => v.name === 'Child')!;
  const targets = availableLinkTargets(parsed, child as any);
  assert.strictEqual(targets.length, 0, 'already-linked parent should not be offered again');
});

test('availableLinkTargets excludes self and stays within the same universe (OUTPUT vs INPUT)', () => {
  const parsed = parseSQuiL([
    '--Name: CrossUniverse',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Param_InputThing table(InputThingID int Primary Key, X int);',
    'Declare @Returns_Child table(ChildID int, Note varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n'));

  const child = parsed.variables.find(v => v.name === 'Child')!;
  const targets = availableLinkTargets(parsed, child as any);
  assert.strictEqual(targets.length, 1);
  assert.strictEqual(targets[0].parent.name, 'Parent', 'an INPUT-universe table must not be offered to an OUTPUT child');
});

test('buildInsertLinkColumnEdit appends the link column before the closing paren, and the result re-parses with a new edge', () => {
  const source = [
    '--Name: NeedsLink',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(ChildID int, Note varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const parsed = parseSQuiL(source);
  const child = parsed.variables.find(v => v.name === 'Child')!;
  const targets = availableLinkTargets(parsed, child as any);
  assert.strictEqual(targets.length, 1);

  const edit = buildInsertLinkColumnEdit(source.split('\n'), child as any, targets[0]);
  assert.ok(edit);
  assert.match(edit!.title, /Link to `Parent` via `ParentID`/);

  const updated = applyEdit(source, edit!);
  assert.strictEqual(
    updated.split('\n')[2],
    'Declare @Returns_Child table(ChildID int, Note varchar(50), ParentID int);',
  );

  const reparsed = parseSQuiL(updated);
  const newChild = reparsed.variables.find(v => v.name === 'Child')!;
  assert.ok(newChild.columns!.find(c => c.name === 'ParentID'), 'link column should now be present on Child');

  // No more available link targets — the relationship is wired.
  const remaining = availableLinkTargets(reparsed, newChild as any);
  assert.strictEqual(remaining.length, 0);
});

test('buildInsertLinkColumnEdit works for a multi-line TABLE(...) declaration', () => {
  const source = [
    '--Name: MultiLineLink',
    'Declare @Returns_Parent table(ParentID int Primary Key, Name varchar(50));',
    'Declare @Returns_Child table(',
    '  ChildID int,',
    '  Note varchar(50)',
    ');',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const parsed = parseSQuiL(source);
  const child = parsed.variables.find(v => v.name === 'Child')!;
  const targets = availableLinkTargets(parsed, child as any);
  assert.strictEqual(targets.length, 1);

  const edit = buildInsertLinkColumnEdit(source.split('\n'), child as any, targets[0]);
  assert.ok(edit);

  const updated = applyEdit(source, edit!);
  const reparsed = parseSQuiL(updated);
  const newChild = reparsed.variables.find(v => v.name === 'Child')!;
  assert.ok(newChild.columns!.find(c => c.name === 'ParentID'));
});

// ── Cursor hit-testing ───────────────────────────────────────────────────────

test('isCursorOnVariable is true anywhere within a single-line declaration, false outside it', () => {
  const source = [
    '--Name: HitTest',
    'Declare @Returns_Child table(ChildID int, Name varchar(50));',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const parsed = parseSQuiL(source);
  const child = parsed.variables.find(v => v.name === 'Child')!;
  const lines = source.split('\n');

  assert.ok(isCursorOnVariable(lines, child, 1));
  assert.ok(!isCursorOnVariable(lines, child, 2));
});

test('isCursorOnVariable spans every line of a multi-line declaration', () => {
  const source = [
    '--Name: HitTestMultiline',
    'Declare @Returns_Child table(',
    '  ChildID int,',
    '  Name varchar(50)',
    ');',
    'Use [Db];',
    'Select 1;',
  ].join('\n');
  const parsed = parseSQuiL(source);
  const child = parsed.variables.find(v => v.name === 'Child')!;
  const lines = source.split('\n');

  assert.ok(isCursorOnVariable(lines, child, 1));
  assert.ok(isCursorOnVariable(lines, child, 3));
  assert.ok(isCursorOnVariable(lines, child, 4));
  assert.ok(!isCursorOnVariable(lines, child, 5));
});
