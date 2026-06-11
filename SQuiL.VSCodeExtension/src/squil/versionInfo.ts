// Pure release-tag logic for the SQuiL update checker. NO `vscode` import, so
// it runs under `node --test`. Mirrors SQuiLVersion.cs in the SSMS and Visual
// Studio extensions — change one, change the others (see CLAUDE.md port table).

export interface ParsedTag {
  /** SDK-version segment, e.g. [10, 0, 100] from "10.0.100.0042-beta". */
  sdk: number[];
  /** Build segment (github.run_number), e.g. 42 from ".0042". */
  build: number;
  /** True when the tag carries a prerelease suffix such as "-beta". */
  prerelease: boolean;
}

export interface ReleaseInfo {
  tag: string;
  prerelease: boolean;
  htmlUrl: string;
  /** Whether this release carries this surface's expected .vsix asset. */
  hasAsset: boolean;
}

export interface UpdateResult {
  tag: string;
  htmlUrl: string;
}

// <sdk>.<build>[-suffix] — sdk is dotted (10.0.100), build is the last numeric
// segment, suffix (if any) marks a prerelease.
const TAG_RE = /^(\d+(?:\.\d+)*)\.(\d+)(-[0-9A-Za-z.-]+)?$/;

export function parseTag(tag: string): ParsedTag | undefined {
  if (!tag) return undefined;
  const m = TAG_RE.exec(tag.trim());
  if (!m) return undefined;
  const sdk = m[1].split('.').map(n => parseInt(n, 10));
  if (sdk.some(n => Number.isNaN(n))) return undefined;
  const build = parseInt(m[2], 10);
  if (Number.isNaN(build)) return undefined;
  return { sdk, build, prerelease: m[3] !== undefined };
}

/** -1 if a<b, 0 if equal, 1 if a>b. SDK segments first, then build. */
export function compareTags(a: ParsedTag, b: ParsedTag): number {
  const len = Math.max(a.sdk.length, b.sdk.length);
  for (let i = 0; i < len; i++) {
    const x = a.sdk[i] ?? 0;
    const y = b.sdk[i] ?? 0;
    if (x !== y) return x < y ? -1 : 1;
  }
  if (a.build !== b.build) return a.build < b.build ? -1 : 1;
  return 0;
}

export function isDevTag(tag: string): boolean {
  return /^__.*__$/.test(tag) || parseTag(tag) === undefined;
}

/**
 * Newest applicable release strictly newer than `currentTag`, or undefined.
 * Stable channel (current tag not prerelease) ignores prerelease releases;
 * prerelease channel considers both. Releases without the expected asset are
 * skipped so a partially-published release never registers as an update.
 */
export function selectUpdate(currentTag: string, releases: ReleaseInfo[]): UpdateResult | undefined {
  const current = parseTag(currentTag);
  if (!current) return undefined; // dev/unstamped build — caller handles
  const onPrerelease = current.prerelease;

  let bestParsed: ParsedTag | undefined;
  let best: ReleaseInfo | undefined;
  for (const r of releases) {
    if (!r.hasAsset) continue;
    if (!onPrerelease && r.prerelease) continue;
    const p = parseTag(r.tag);
    if (!p) continue;
    if (compareTags(p, current) <= 0) continue;
    if (!bestParsed || compareTags(p, bestParsed) > 0) { bestParsed = p; best = r; }
  }
  return best ? { tag: best.tag, htmlUrl: best.htmlUrl } : undefined;
}
