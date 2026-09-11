// RELEASE_TAG is stamped at publish time by .github/workflows/publish.yml,
// which replaces the placeholder below with the GitHub release tag — a beta is
// "<version>-beta.<run#>" (e.g. "1.0.0-beta.188"), a stable release is bare
// (e.g. "1.0.0"). Local/dev builds keep the placeholder; the update checker
// treats that as a dev build and skips the automatic check.
export const RELEASE_TAG = '__SQUIL_RELEASE_TAG__';
