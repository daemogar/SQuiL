using Xunit;

namespace SQuiL.Tests.Postgres;

/// <summary>
/// Task 8 (Phase 3 Postgres): <c>[SQuiLQueryTransaction]</c>'s C# wrapping — injecting
/// <c>connection.BeginTransaction()</c> and committing/rolling back against the abstract
/// <c>DbTransaction</c> (Npgsql implements it as <c>NpgsqlTransaction : DbTransaction</c>) — is
/// dialect-neutral, emitted the same way regardless of the resolved dialect
/// (<see cref="SQuiL.Tests.TransactionInjectionTests"/>,
/// <see cref="SQuiL.Tests.TransactionDebugRollbackTests"/>,
/// <see cref="SQuiL.Tests.TransactionEnabledPositionalTests"/> cover the SQL Server shapes;
/// <see cref="SQuiL.Tests.Sqlite.SqliteTransactionTests"/> proves it at runtime for SQLite).
///
/// These are SNAPSHOT verifications, not runtime round trips: PostgreSQL has no in-process
/// embeddable engine, so a live-DB proof needs a real server (Testcontainers, tracked separately
/// as TODO #8/3C). Each fixture below is the direct PostgreSQL twin — ported to
/// <c>Create Temp Table</c> + PG type spellings — of the SQLite runtime fixtures in
/// <c>SQuiL.Tests/Sqlite/Queries/SqliteTxnCommit.sql</c> / <c>SqliteTxnDebug.sql</c>, plus the
/// SQL Server <c>enabled:false</c> snapshot in <c>TransactionEnabledPositionalTests</c>. Accepting
/// these snapshots proves PostgreSQL gets the SAME transaction wrapping (or its deliberate
/// absence) as every other dialect.
/// </summary>
public class PostgresTransactionTests
{
	/// <summary>
	/// Default <c>[SQuiLQueryTransaction]</c> (enabled:true, no <c>@Debug</c>): the emitted
	/// <c>Process…Async</c> must open a <c>DbTransaction</c>, wrap the real-table
	/// <c>Insert Into Widgets</c> mutation, and commit on success — the PostgreSQL twin of
	/// <see cref="SQuiL.Tests.Sqlite.SqliteTransactionTests.Successful_transaction_commits_the_real_table_mutation"/>.
	/// </summary>
	[Fact]
	public System.Threading.Tasks.Task Transaction_commits_the_real_table_mutation()
	{
		var name = nameof(Transaction_commits_the_real_table_mutation);
		var source = $$"""
			using Microsoft.Extensions.Configuration;
			using SQuiL;

			namespace TestCase;

			[SQuiLDialect(SQuiLDialect.Postgres)]
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext(IConfiguration Configuration) : PostgresDataContext(Configuration)
			{
			}
			""";
		return TestHelper.VerifyPostgres([source], [$$"""
			--Name: {{name}}
			Create Temp Table Params_Widget (WidgetID int Primary Key, Name text);
			Create Temp Table Return_Inserted (Inserted int);
			Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;
			Insert Into Return_Inserted (Inserted) Select Count(*) From Params_Widget;
			Select Inserted From Return_Inserted;
			"""]);
	}

	/// <summary>
	/// <c>@Debug</c> declared, <c>debugRollback</c> default (true): the emitted code must gate the
	/// commit on the debug expression, rolling back the real-table mutation while STILL reading and
	/// returning the response (dry-run semantics) — the PostgreSQL twin of
	/// <see cref="SQuiL.Tests.Sqlite.SqliteTransactionTests.Debug_dry_run_rolls_back_but_still_returns_the_response"/>.
	/// </summary>
	[Fact]
	public System.Threading.Tasks.Task Debug_declared_gates_commit_on_the_debug_expression()
	{
		var name = nameof(Debug_declared_gates_commit_on_the_debug_expression);
		var source = $$"""
			using Microsoft.Extensions.Configuration;
			using SQuiL;

			namespace TestCase;

			[SQuiLDialect(SQuiLDialect.Postgres)]
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext(IConfiguration Configuration) : PostgresDataContext(Configuration)
			{
			}
			""";
		return TestHelper.VerifyPostgres([source], [$$"""
			--Name: {{name}}
			Create Temp Table Debug (Value int);
			Create Temp Table Params_Widget (WidgetID int Primary Key, Name text);
			Create Temp Table Return_Inserted (Inserted int);
			Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;
			Insert Into Return_Inserted (Inserted) Select Count(*) From Params_Widget;
			Select Inserted From Return_Inserted;
			"""]);
	}

	/// <summary>
	/// <c>enabled:false</c>: the caller owns the transaction externally, so the generator must
	/// NOT inject a <c>DbTransaction</c>/<c>BeginTransaction</c> wrapper at all — the PostgreSQL
	/// twin of <see cref="SQuiL.Tests.TransactionEnabledPositionalTests.PositionalEnabledFalse_NoTransactionInjected"/>.
	/// </summary>
	[Fact]
	public System.Threading.Tasks.Task Enabled_false_injects_no_transaction_wrapper()
	{
		var name = nameof(Enabled_false_injects_no_transaction_wrapper);
		var source = $$"""
			using Microsoft.Extensions.Configuration;
			using SQuiL;

			namespace TestCase;

			[SQuiLDialect(SQuiLDialect.Postgres)]
			[SQuiLQueryTransaction(QueryFiles.{{name}}, enabled: false)]
			public partial class {{name}}DataContext(IConfiguration Configuration) : PostgresDataContext(Configuration)
			{
			}
			""";
		return TestHelper.VerifyPostgres([source], [$$"""
			--Name: {{name}}
			Create Temp Table Params_Widget (WidgetID int Primary Key, Name text);
			Insert Into Widgets (WidgetID, Name) Select WidgetID, Name From Params_Widget;
			"""]);
	}
}
