import { test } from 'node:test';
import * as assert from 'node:assert';
import { parseTag, compareTags, selectUpdate, isDevTag, ReleaseInfo } from './versionInfo';

test('parseTag reads sdk, build, and prerelease', () => {
  const p = parseTag('10.0.100.0042-beta');
  assert.deepStrictEqual(p?.sdk, [10, 0, 100]);
  assert.strictEqual(p?.build, 42);
  assert.strictEqual(p?.prerelease, true);
});

test('parseTag treats a tag with no suffix as stable', () => {
  const p = parseTag('10.0.100.0050');
  assert.strictEqual(p?.prerelease, false);
  assert.strictEqual(p?.build, 50);
});

test('parseTag rejects garbage', () => {
  assert.strictEqual(parseTag('not-a-tag'), undefined);
  assert.strictEqual(parseTag(''), undefined);
});

test('compareTags orders by sdk then build', () => {
  assert.strictEqual(compareTags(parseTag('10.0.100.0042')!, parseTag('10.0.100.0050')!), -1);
  assert.strictEqual(compareTags(parseTag('10.0.200.0001')!, parseTag('10.0.100.9999')!), 1);
  assert.strictEqual(compareTags(parseTag('10.0.100.0042')!, parseTag('10.0.100.0042')!), 0);
});

const releases: ReleaseInfo[] = [
  { tag: '10.0.100.0060-beta', prerelease: true,  htmlUrl: 'u60', hasAsset: true },
  { tag: '10.0.100.0055',      prerelease: false, htmlUrl: 'u55', hasAsset: true },
  { tag: '10.0.100.0050-beta', prerelease: true,  htmlUrl: 'u50', hasAsset: true },
  { tag: '10.0.100.0070-beta', prerelease: true,  htmlUrl: 'u70', hasAsset: false },
];

test('stable channel ignores betas, picks newest stable', () => {
  const r = selectUpdate('10.0.100.0040', releases);
  assert.strictEqual(r?.tag, '10.0.100.0055');
});

test('stable channel returns undefined when already newest stable', () => {
  assert.strictEqual(selectUpdate('10.0.100.0055', releases), undefined);
});

test('prerelease channel sees both channels and skips assetless releases', () => {
  const r = selectUpdate('10.0.100.0052-beta', releases);
  assert.strictEqual(r?.tag, '10.0.100.0060-beta');
});

test('isDevTag flags placeholder and unparseable tags', () => {
  assert.strictEqual(isDevTag('__SQUIL_RELEASE_TAG__'), true);
  assert.strictEqual(isDevTag('garbage'), true);
  assert.strictEqual(isDevTag('10.0.100.0042-beta'), false);
  assert.strictEqual(isDevTag('1.0.0-beta.123'), false);
  assert.strictEqual(isDevTag('1.0.0'), false);
});

// ── SemVer scheme: <next>-beta.<run#> betas, plain MAJOR.MINOR.PATCH officials ──

test('parseTag reads a semver beta as prerelease', () => {
  const p = parseTag('1.0.0-beta.123');
  assert.strictEqual(p?.prerelease, true);
});

test('compareTags ranks an official above its own betas', () => {
  assert.strictEqual(compareTags(parseTag('1.0.0')!, parseTag('1.0.0-beta.123')!), 1);
  assert.strictEqual(compareTags(parseTag('1.0.0-beta.123')!, parseTag('1.0.0')!), -1);
});

test('compareTags orders betas by run number', () => {
  assert.strictEqual(compareTags(parseTag('1.0.0-beta.124')!, parseTag('1.0.0-beta.123')!), 1);
  assert.strictEqual(compareTags(parseTag('1.0.0-beta.9')!, parseTag('1.0.0-beta.10')!), -1);
  assert.strictEqual(compareTags(parseTag('1.0.0-beta.123')!, parseTag('1.0.0-beta.123')!), 0);
});

test('compareTags ranks the next cycle beta above the prior official', () => {
  assert.strictEqual(compareTags(parseTag('1.0.1-beta.1')!, parseTag('1.0.0')!), 1);
});

const semverReleases: ReleaseInfo[] = [
  { tag: '1.0.0-beta.120', prerelease: true,  htmlUrl: 'b120', hasAsset: true },
  { tag: '1.0.0-beta.125', prerelease: true,  htmlUrl: 'b125', hasAsset: true },
  { tag: '1.0.0',          prerelease: false, htmlUrl: 'v100', hasAsset: true },
];

test('beta user is offered the official release', () => {
  const r = selectUpdate('1.0.0-beta.125', semverReleases);
  assert.strictEqual(r?.tag, '1.0.0');
});

test('beta user is offered a newer beta when no official exists yet', () => {
  const betasOnly = semverReleases.filter(r => r.prerelease);
  const r = selectUpdate('1.0.0-beta.120', betasOnly);
  assert.strictEqual(r?.tag, '1.0.0-beta.125');
});

test('stable user on the official is not offered anything', () => {
  assert.strictEqual(selectUpdate('1.0.0', semverReleases), undefined);
});
