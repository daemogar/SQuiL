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
