import { test } from 'node:test';
import * as assert from 'node:assert';
import { resolveContext } from './contextResolver';

// ─── In-memory fake filesystem helpers ─────────────────────────────────────

/** Build a readFile stub from a files map. */
function makeReadFile(files: Record<string, string>): (p: string) => string | undefined {
  return (p: string) => files[p.replace(/\\/g, '/')];
}

/** Build a listDir stub from a files map. Lists both direct-child files and immediate subdirectory names. */
function makeListDir(files: Record<string, string>): (d: string) => string[] {
  return (d: string) => {
    const dir = d.replace(/\\/g, '/').replace(/\/$/, '');
    const result = new Set<string>();

    for (const rawKey of Object.keys(files)) {
      const f = rawKey.replace(/\\/g, '/');
      if (!f.startsWith(dir + '/')) continue;
      const rest = f.slice(dir.length + 1); // rest after "dir/"
      const nextSlash = rest.indexOf('/');
      if (nextSlash === -1) {
        // direct-child file
        result.add(rest);
      } else {
        // first path component = a subdirectory name
        result.add(rest.slice(0, nextSlash));
      }
    }

    return Array.from(result);
  };
}

// ─── Core resolution tests ──────────────────────────────────────────────────

test('resolves single SQuiLQueryTransaction with named args', () => {
  const files: Record<string, string> = {
    '/proj/Queries/UpdateDocs.squil': '/* sql */',
    '/proj/MyDataContext.cs':
      '[SQuiLQueryTransaction(QueryFiles.QueriesUpdateDocs, debugRollback: false)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/UpdateDocs.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true, 'found should be true');
  assert.strictEqual(r.matchCount, 1, 'matchCount should be 1');
  assert.strictEqual(r.attribute, 'SQuiLQueryTransaction', 'attribute should be SQuiLQueryTransaction');
  assert.strictEqual(r.debugRollback, false, 'debugRollback: false was explicit');
  assert.strictEqual(r.enabled, true, 'enabled defaults to true when not specified');
});

test('resolves single SQuiLQuery with explicit enabled: false', () => {
  const files: Record<string, string> = {
    '/proj/Queries/GetDocs.squil': '/* sql */',
    '/proj/MyDataContext.cs':
      '[SQuiLQuery(QueryFiles.QueriesGetDocs, enabled: false)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/GetDocs.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true);
  assert.strictEqual(r.matchCount, 1);
  assert.strictEqual(r.attribute, 'SQuiLQuery');
  assert.strictEqual(r.enabled, false, 'enabled: false was explicit');
  assert.strictEqual(r.debugRollback, true, 'debugRollback defaults to true');
});

test('orphan file → not found (matchCount 0)', () => {
  const files: Record<string, string> = {
    '/proj/Queries/Orphan.squil': '/* sql */',
    '/proj/MyDataContext.cs': '// no SQuiLQuery attribute here',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/Orphan.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, false, 'found should be false when no .cs references the member');
  assert.strictEqual(r.matchCount, 0, 'matchCount should be 0');
  assert.strictEqual(r.attribute, undefined, 'attribute should be undefined');
  assert.strictEqual(r.enabled, false, 'enabled falls back to false');
  assert.strictEqual(r.debugRollback, true, 'debugRollback defaults to true');
});

test('two contexts referencing same member → matchCount 2', () => {
  const files: Record<string, string> = {
    '/proj/Queries/UpdateDocs.squil': '/* sql */',
    '/proj/ContextA.cs': '[SQuiLQueryTransaction(QueryFiles.QueriesUpdateDocs)]',
    '/proj/ContextB.cs': '[SQuiLQuery(QueryFiles.QueriesUpdateDocs)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/UpdateDocs.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, false, 'found is false when matchCount > 1');
  assert.strictEqual(r.matchCount, 2, 'matchCount should be 2');
});

// ─── Member naming tests ────────────────────────────────────────────────────

test('member name: flat file (no subdirectory) after stripping .squil', () => {
  // /proj/UpdateDocs.squil → member QueriesUpdateDocs is NOT right;
  // relative path from csproj dir is just "UpdateDocs.squil" → member "UpdateDocs"
  const files: Record<string, string> = {
    '/proj/UpdateDocs.squil': '/* sql */',
    '/proj/MyDataContext.cs': '[SQuiLQuery(QueryFiles.UpdateDocs)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/UpdateDocs.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true, 'should resolve bare member UpdateDocs');
  assert.strictEqual(r.matchCount, 1);
});

test('member name: nested path preserves verbatim casing', () => {
  // /proj/sub/Foo.squil → relative from /proj → sub/Foo.squil → strip sep → subFoo
  const files: Record<string, string> = {
    '/proj/sub/Foo.squil': '/* sql */',
    '/proj/MyDataContext.cs': '[SQuiLQuery(QueryFiles.subFoo)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/sub/Foo.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true, 'verbatim-concat member subFoo should match');
  assert.strictEqual(r.matchCount, 1);
});

test('member name: .sql extension also stripped', () => {
  const files: Record<string, string> = {
    '/proj/Queries/GetData.sql': '/* sql */',
    '/proj/MyDataContext.cs': '[SQuiLQuery(QueryFiles.QueriesGetData)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/GetData.sql',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true, '.sql extension should be stripped');
  assert.strictEqual(r.matchCount, 1);
});

// ─── Enabled/debugRollback default tests ────────────────────────────────────

test('SQuiLQueryTransaction with no named args defaults: enabled=true, debugRollback=true', () => {
  const files: Record<string, string> = {
    '/proj/Queries/Run.squil': '/* sql */',
    '/proj/MyDataContext.cs': '[SQuiLQueryTransaction(QueryFiles.QueriesRun)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/Run.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true);
  assert.strictEqual(r.enabled, true, '[SQuiLQueryTransaction] defaults enabled=true');
  assert.strictEqual(r.debugRollback, true, '[SQuiLQueryTransaction] defaults debugRollback=true');
});

test('SQuiLQuery with no named args defaults: enabled=false, debugRollback=true', () => {
  // [SQuiLQuery] never wraps in a transaction → enabled defaults to false
  const files: Record<string, string> = {
    '/proj/Queries/Get.squil': '/* sql */',
    '/proj/MyDataContext.cs': '[SQuiLQuery(QueryFiles.QueriesGet)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/Get.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true);
  assert.strictEqual(r.enabled, false, '[SQuiLQuery] defaults enabled=false (no transaction)');
  assert.strictEqual(r.debugRollback, true);
});

test('SQuiLQueryTransaction with enabled: false explicit', () => {
  const files: Record<string, string> = {
    '/proj/Queries/DoWork.squil': '/* sql */',
    '/proj/MyDataContext.cs': '[SQuiLQueryTransaction(QueryFiles.QueriesDoWork, enabled: false, debugRollback: true)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/DoWork.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true);
  assert.strictEqual(r.enabled, false, 'explicit enabled:false overrides the transaction default');
  assert.strictEqual(r.debugRollback, true);
});

// ─── .csproj detection (walk upward) ─────────────────────────────────────────

test('walks up two levels to find the .csproj', () => {
  const files: Record<string, string> = {
    '/root/proj/deep/nested/Query.squil': '/* sql */',
    '/root/proj/MyDataContext.cs': '[SQuiLQuery(QueryFiles.deepnestedQuery)]',
    '/root/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/root/proj/deep/nested/Query.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true, 'should find context 2 levels up from csproj dir');
  assert.strictEqual(r.matchCount, 1);
});

test('matches attribute with extra whitespace and quotes in named args', () => {
  const files: Record<string, string> = {
    '/proj/Queries/FetchData.squil': '/* sql */',
    '/proj/MyDataContext.cs':
      '[SQuiLQueryTransaction(QueryFiles.QueriesFetchData, enabled: true, debugRollback: false)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/FetchData.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true);
  assert.strictEqual(r.enabled, true);
  assert.strictEqual(r.debugRollback, false);
});

// ─── CS files in subdirectory of project ─────────────────────────────────────

test('scans CS files recursively in project subdirectories', () => {
  const files: Record<string, string> = {
    '/proj/Queries/SaveRecord.squil': '/* sql */',
    '/proj/DataContexts/SaveRecordContext.cs':
      '[SQuiLQueryTransaction(QueryFiles.QueriesSaveRecord)]',
    '/proj/proj.csproj': '',
  };

  const r = resolveContext(
    '/proj/Queries/SaveRecord.squil',
    makeReadFile(files),
    makeListDir(files),
  );

  assert.strictEqual(r.found, true, 'should scan subdirectories for .cs files');
  assert.strictEqual(r.matchCount, 1);
});
