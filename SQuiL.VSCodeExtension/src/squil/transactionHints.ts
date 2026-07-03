/**
 * Transaction-hint pass (SP0026).
 *
 * Produces a plain-data hint descriptor when a .squil file is registered by a
 * [SQuiLQueryTransaction] attribute that has `debugRollback: true` (the default)
 * but the file does NOT declare `@Debug`.  Without `@Debug` the debug-rollback
 * path is unreachable — the setting is inert.
 *
 * Trigger:  resolved context found + attribute is SQuiLQueryTransaction +
 *           debugRollback is true + no `@Debug` variable declared.
 * Severity: VS Code Hint; C# extension Info (no Hint in their enum).
 *
 * The caller (diagnosticsProvider) converts the descriptor into a
 * vscode.Diagnostic object; unit tests consume the raw descriptor directly —
 * no vscode dependency here.
 *
 * Port: SQuiL.SsmsExtension/Parsing/SQuiLLinter.LintDebugRollbackHint and
 *       SQuiL.VisualStudioExtension/Parsing/SQuiLLinter.LintDebugRollbackHint —
 *       change one side, change all three.
 */

import { SQuiLParseResult } from './parser';
import { ResolvedContext } from './contextResolver';

export interface TransactionHint {
  code: 'SP0026';
  message: string;
  /** Zero-based line of the first character in the file (file-level hint). */
  line: 0;
  character: 0;
}

/**
 * Return SP0026 hint descriptors for the given parse result and resolved context.
 *
 * Returns an empty array when the hint does not apply.
 */
export function transactionHints(
  parsed: SQuiLParseResult,
  resolved: ResolvedContext,
): TransactionHint[] {
  // Only fires when:
  // 1. A context was found (not orphan / duplicate — those have their own diagnostics).
  // 2. The attribute is [SQuiLQueryTransaction] (never [SQuiLQuery] — it has no transaction).
  // 3. debugRollback is true (the default; if the author explicitly set debugRollback:false
  //    the hint would be incorrect).
  // 4. @Debug is NOT declared — without it the debug-rollback branch is unreachable.
  if (!resolved.found) return [];
  if (resolved.attribute !== 'SQuiLQueryTransaction') return [];
  if (!resolved.enabled) return [];
  if (!resolved.debugRollback) return [];

  const hasDebug = parsed.variables.some(v => v.role === 'debug');
  if (hasDebug) return [];

  return [
    {
      code: 'SP0026',
      message:
        '`debugRollback: true` has no effect without a declared `@Debug`. ' +
        'Declare `@Debug bit;` in the header, or set `debugRollback: false` on [SQuiLQueryTransaction].',
      line: 0,
      character: 0,
    },
  ];
}
