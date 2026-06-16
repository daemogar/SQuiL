import { test } from 'node:test';
import * as assert from 'node:assert';
import { validateVariables } from './variableValidator';

// Mirrors VariableValidatorTests.cs in SQuiL.Tests — change one, change the other.

test('valid file has no findings', () => {
  const findings = validateVariables([
    'Declare @Debug bit = 0;',
    'Declare @Param_Name varchar(100);',
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Set @Return_Count = (Select Count(*) From Users Where Name = @Param_Name);',
    'Select @Return_Count;',
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('reference never declared is flagged', () => {
  const findings = validateVariables([
    'Declare @Param_PersonID varchar(10);',
    'Use MyDatabase;',
    'Select * From People Where PersonID = @PersonID;',
  ].join('\n'));

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].kind, 'undeclared');
  assert.strictEqual(findings[0].name, '@PersonID');
  assert.strictEqual(findings[0].line, 2);
});

test('reference before declaration is flagged as used-before-declared', () => {
  const findings = validateVariables([
    "Set @Param_Name = 'x';",
    'Declare @Param_Name varchar(100);',
    'Use MyDatabase;',
  ].join('\n'));

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].kind, 'usedBeforeDeclared');
  assert.strictEqual(findings[0].name, '@Param_Name');
  assert.strictEqual(findings[0].line, 0);
});

test('@Debug and @EnvironmentName require declaration like any variable', () => {
  const findings = validateVariables([
    'Declare @Param_Name varchar(100);',
    'Use MyDatabase;',
    'If @Debug = 1 Select @EnvironmentName;',
  ].join('\n'));

  assert.strictEqual(findings.length, 2);
  assert.strictEqual(findings[0].kind, 'undeclared');
  assert.strictEqual(findings[0].name, '@Debug');
  assert.strictEqual(findings[1].name, '@EnvironmentName');
});

test('special declared after use is flagged', () => {
  const findings = validateVariables([
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Declare @Debug bit = 0;',
    'If @Debug = 1 Select 1;',
  ].join('\n'));

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].kind, 'specialAfterUse');
  assert.strictEqual(findings[0].name, '@Debug');
  assert.strictEqual(findings[0].line, 2);
});

test('special declared after other header declarations is flagged not-first', () => {
  const findings = validateVariables([
    'Declare @Param_Name varchar(100);',
    'Declare @Debug bit = 0;',
    'Use MyDatabase;',
  ].join('\n'));

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].kind, 'specialNotFirst');
  assert.strictEqual(findings[0].name, '@Debug');
  assert.strictEqual(findings[0].line, 1);
});

test('both specials first in any order are fine', () => {
  const findings = validateVariables([
    'Declare @EnvironmentName varchar(50);',
    'Declare @Debug bit = 0;',
    'Declare @Param_Name varchar(100);',
    'Use MyDatabase;',
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('multi-variable declare with defaults declares all names', () => {
  const findings = validateVariables([
    'Declare @Param_Debug bit = 1,',
    '        @Param_PersonID varchar(10),',
    '        @Param_CourseCode varchar(20) = Null;',
    'Use MyDatabase;',
    'Select @Param_Debug, @Param_PersonID, @Param_CourseCode;',
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('reference inside declare default expression is validated', () => {
  const findings = validateVariables([
    'Declare @Params_Terms table(TermCode varchar(10));',
    'Use MyDatabase;',
    'Declare @POS int = (Select POS From @Terms Where IsCurrent = 1);',
  ].join('\n'));

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].kind, 'undeclared');
  assert.strictEqual(findings[0].name, '@Terms');
});

test('table declare without semicolon followed by statement works', () => {
  const findings = validateVariables([
    'Declare @Debug bit = 0;',
    'Use MyDatabase;',
    'Begin',
    '  Declare @Courses table(',
    '    SectionID varchar(20),',
    '    Credits decimal(10,4))',
    '  Insert Into @Courses',
    '  Select SectionID, Credits From Sections',
    'End;',
    'Select * From @Courses;',
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('system variables, strings, comments, and brackets are ignored', () => {
  const findings = validateVariables([
    'Declare @Debug bit = 0;',
    'Use MyDatabase;',
    '-- comment mentions @NotReal1',
    '/* block comment @NotReal2 /* nested @NotReal3 */ still comment */',
    "Select 'literal @NotReal4' As [@NotReal5], @@ROWCOUNT;",
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('variable names are case-insensitive', () => {
  const findings = validateVariables([
    'Declare @Param_Name varchar(100);',
    'Use MyDatabase;',
    'Select @PARAM_NAME;',
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('every occurrence of an undeclared reference is reported', () => {
  const findings = validateVariables([
    'Declare @Debug bit = 0;',
    'Use MyDatabase;',
    'Select @Missing;',
    'Select @Missing;',
  ].join('\n'));

  assert.strictEqual(findings.length, 2);
  assert.ok(findings.every((f) => f.name === '@Missing'));
});

test('case expression in declare default does not end the declare list', () => {
  const findings = validateVariables([
    'Declare @Debug bit = 0;',
    'Use MyDatabase;',
    'Declare @A int = Case When 1 = 1 Then 1 Else 0 End, @B int = @A;',
    'Select @A, @B;',
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('character position is reported 0-based', () => {
  const findings = validateVariables('Use MyDatabase;\r\nSelect @X;');

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].line, 1);
  assert.strictEqual(findings[0].character, 7);
});

test('@SuppressDebug and @AsOfDate are recognized specials (no findings when valid)', () => {
  const findings = validateVariables([
    'Declare @Debug bit = 1;',
    'Declare @SuppressDebug bit = 0;',
    'Declare @AsOfDate date = \'2008-10-01\';',
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Set @Return_Count = (Select Count(*) From Logs Where CreatedOn <= @AsOfDate);',
    'Select @Return_Count;',
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('@SuppressDebug without @Debug is flagged suppressDebugWithoutDebug', () => {
  const findings = validateVariables([
    'Declare @SuppressDebug bit = 0;',
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Select @Return_Count = 1;',
    'Select @Return_Count;',
  ].join('\n'));

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].kind, 'suppressDebugWithoutDebug');
  assert.strictEqual(findings[0].name, '@SuppressDebug');
  assert.strictEqual(findings[0].line, 0);
});

test('@SuppressDebug with @Debug present does not fire suppressDebugWithoutDebug', () => {
  const findings = validateVariables([
    'Declare @Debug bit = 1;',
    'Declare @SuppressDebug bit = 0;',
    'Use MyDatabase;',
    'Select 1;',
  ].join('\n'));

  assert.deepStrictEqual(findings, []);
});

test('placement rules apply to the new specials (@AsOfDate after use)', () => {
  const findings = validateVariables([
    'Declare @Return_Count int;',
    'Use MyDatabase;',
    'Declare @AsOfDate date = \'2008-10-01\';',
    'Select @Return_Count = 1;',
  ].join('\n'));

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].kind, 'specialAfterUse');
  assert.strictEqual(findings[0].name, '@AsOfDate');
  assert.strictEqual(findings[0].line, 2);
});

test('placement rules apply to the new specials (@SuppressDebug not first)', () => {
  const findings = validateVariables([
    'Declare @Debug bit = 1;',
    'Declare @Param_Name varchar(100);',
    'Declare @SuppressDebug bit = 0;',
    'Use MyDatabase;',
    'Select @Param_Name;',
  ].join('\n'));

  assert.strictEqual(findings.length, 1);
  assert.strictEqual(findings[0].kind, 'specialNotFirst');
  assert.strictEqual(findings[0].name, '@SuppressDebug');
  assert.strictEqual(findings[0].line, 2);
});
