import * as vscode from 'vscode';
import * as https from 'https';

import { RELEASE_TAG } from '../buildInfo';
import { parseTag, selectUpdate, isDevTag, ReleaseInfo } from '../squil/versionInfo';

const RELEASES_URL = 'https://api.github.com/repos/daemogar/SQuiL/releases';
const ASSET_PREFIX = 'squil-editor-';
const THROTTLE_KEY = 'squil.lastUpdateCheck';
const THROTTLE_MS = 24 * 60 * 60 * 1000;

function httpGetJson(url: string): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const req = https.get(
      url,
      { headers: { 'User-Agent': 'SQuiL-vscode', Accept: 'application/vnd.github+json' } },
      res => {
        const status = res.statusCode ?? 0;
        if (status < 200 || status >= 300) {
          res.resume();
          reject(new Error(`GitHub returned HTTP ${status}`));
          return;
        }
        let body = '';
        res.setEncoding('utf8');
        res.on('data', c => (body += c));
        res.on('end', () => {
          try { resolve(JSON.parse(body)); } catch (e) { reject(e); }
        });
      },
    );
    req.on('error', reject);
    req.setTimeout(10000, () => req.destroy(new Error('Request timed out')));
  });
}

function toReleaseInfos(raw: unknown): ReleaseInfo[] {
  if (!Array.isArray(raw)) return [];
  return raw.map((r: any) => ({
    tag: String(r?.tag_name ?? ''),
    prerelease: Boolean(r?.prerelease),
    htmlUrl: String(r?.html_url ?? ''),
    hasAsset:
      Array.isArray(r?.assets) &&
      r.assets.some((a: any) => {
        const name = String(a?.name ?? '');
        return name.startsWith(ASSET_PREFIX) && name.endsWith('.vsix');
      }),
  }));
}

/**
 * Run the update check. `manual` checks ignore the disable setting and the
 * 24h throttle and always report a result; automatic checks fail silently.
 */
export async function checkForUpdates(
  context: vscode.ExtensionContext,
  opts: { manual: boolean },
): Promise<void> {
  const enabled = vscode.workspace.getConfiguration('squil').get<boolean>('checkForUpdates', true);
  if (!opts.manual && !enabled) return;

  if (isDevTag(RELEASE_TAG)) {
    if (opts.manual) {
      void vscode.window.showInformationMessage(
        'SQuiL: this is a local/dev build — update checks compare against released builds only.',
      );
    }
    return;
  }

  const onPrerelease = parseTag(RELEASE_TAG)?.prerelease ?? false;
  if (!opts.manual && onPrerelease) {
    const last = context.globalState.get<number>(THROTTLE_KEY, 0);
    if (Date.now() - last < THROTTLE_MS) return;
  }

  let releases: ReleaseInfo[];
  try {
    releases = toReleaseInfos(await httpGetJson(RELEASES_URL));
  } catch (err) {
    if (opts.manual) {
      const msg = err instanceof Error ? err.message : String(err);
      void vscode.window.showWarningMessage(`SQuiL: couldn't reach GitHub to check for updates. ${msg}`);
    }
    return;
  } finally {
    if (!opts.manual && onPrerelease) {
      void context.globalState.update(THROTTLE_KEY, Date.now());
    }
  }

  const update = selectUpdate(RELEASE_TAG, releases);
  if (!update) {
    if (opts.manual) {
      void vscode.window.showInformationMessage('SQuiL: you are running the latest version.');
    }
    return;
  }

  const choice = await vscode.window.showInformationMessage(
    `SQuiL ${update.tag} is available.`,
    'View Release',
    'Dismiss',
  );
  if (choice === 'View Release') {
    void vscode.env.openExternal(vscode.Uri.parse(update.htmlUrl));
  }
}
