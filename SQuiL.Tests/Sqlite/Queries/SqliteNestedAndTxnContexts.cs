using Microsoft.Extensions.Configuration;

using SQuiL;

namespace SQuiL.Tests.Sqlite;

// Task 9 (Phase 3B) real compiled SQLite data contexts: nested-object stitch (in/out) and
// [SQuiLQueryTransaction] over Microsoft.Data.Sqlite. Each maps one-to-one to a
// Sqlite\Queries\*.sql file (the QueryFiles enum member is the bare file name; this .cs lives in
// the same directory so the generator's path-flattening strips down to it). All are
// [SQuiLDialect(SQuiLDialect.Sqlite)] so the generator emits the SQLite runtime base.

// Nested output: Returns_Order (list root) -> Returns_Line (list child, FK OrderID).
[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.SqliteNestedOutput)]
public partial class SqliteNestedOutputDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

// Nested input: Param_Order (object root) -> Params_Line (list child, FK OrderID); keys synthesized.
[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.SqliteNestedInput)]
public partial class SqliteNestedInputDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

// Transaction commit-on-success (mutates a real, non-temp table).
[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQueryTransaction(QueryFiles.SqliteTxnCommit)]
public partial class SqliteTxnCommitDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

// Transaction rollback-on-error (real-table insert then a SQLite error).
[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQueryTransaction(QueryFiles.SqliteTxnRollback)]
public partial class SqliteTxnRollbackDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

// Transaction debug dry-run (@Debug declared, debugRollback default true): rolls back the
// real-table mutation but STILL returns the response.
[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQueryTransaction(QueryFiles.SqliteTxnDebug)]
public partial class SqliteTxnDebugDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);
