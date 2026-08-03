using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace SQuiL.Tests.Sqlite;

/// <summary>
/// Task 9 (Phase 3B): proves <c>[SQuiLQueryTransaction]</c> works end-to-end for SQLite. The
/// transaction wrapper is dialect-agnostic — the generator emits
/// <c>connection.BeginTransaction()</c> and <c>transaction.Commit()/Rollback()</c> against the
/// abstract <c>DbTransaction</c>, which Microsoft.Data.Sqlite implements as
/// <c>SqliteTransaction : DbTransaction</c>. These facts drive a REAL (non-temp) table so the
/// commit/rollback effect is observable across connections.
///
/// The mutation targets a real <c>Widgets</c> table (created on the keep-alive connection before
/// the call). Because the database is an in-memory shared-cache DB, a real table is visible to the
/// second connection the generated <c>Process…Async</c> opens — unlike a temp table, which is
/// connection-scoped. Commit/rollback is asserted by re-reading <c>Widgets</c> on the keep-alive
/// connection AFTER the call.
/// </summary>
public class SqliteTransactionTests
{
	private static (SqliteConnection keepAlive, ServiceProvider provider) Arrange(
		string dbName, string? environmentName = null)
	{
		var connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared";

		var keepAlive = new SqliteConnection(connectionString);
		keepAlive.Open();

		// Real (non-temp) table, visible to every connection sharing this in-memory cache.
		using (var create = keepAlive.CreateCommand())
		{
			create.CommandText = "Create Table Widgets (WidgetID INTEGER Primary Key, Name TEXT);";
			create.ExecuteNonQuery();
		}

		var settings = new Dictionary<string, string?>
		{
			["ConnectionStrings:SQuiLDatabase"] = connectionString,
		};
		if (environmentName is not null)
			settings["EnvironmentName"] = environmentName;

		var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

		ResetAddSQuiLGuard();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(config);
		services.AddSQuiL();
		return (keepAlive, services.BuildServiceProvider());
	}

	private static void ResetAddSQuiLGuard()
	{
		var property = typeof(SQuiLExtensions).GetProperty(
			nameof(SQuiLExtensions.IsLoaded),
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
		property!.SetValue(null, false);
	}

	private static long CountWidgets(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "Select Count(*) From Widgets;";
		return (long)command.ExecuteScalar()!;
	}

	/// <summary>
	/// Commit-on-success: an enabled transaction whose body inserts into the real <c>Widgets</c>
	/// table completes without error, so the generator commits — the rows are visible after the call.
	/// </summary>
	[Fact]
	public async Task Successful_transaction_commits_the_real_table_mutation()
	{
		var (keepAlive, provider) = Arrange(nameof(Successful_transaction_commits_the_real_table_mutation));
		using var keep = keepAlive;

		var context = provider.GetRequiredService<SqliteTxnCommitDataContext>();
		var result = await context.ProcessSqliteTxnCommitAsync(new SqliteTxnCommitRequest
		{
			Widget = [new(1, "Ada"), new(2, "Alan")],
		});

		Assert.False(result.TryGetErrors(out _)); // no-return query → non-generic result; success
		Assert.Equal(2, CountWidgets(keepAlive)); // committed
	}

	/// <summary>
	/// Rollback-on-error: the body inserts into <c>Widgets</c> and THEN references a table that does
	/// not exist. The SQLite error is caught, the transaction is rolled back, and the error surfaces
	/// through the result — so the earlier insert is undone (the real table is empty afterwards).
	/// </summary>
	[Fact]
	public async Task Failed_transaction_rolls_back_the_real_table_mutation()
	{
		var (keepAlive, provider) = Arrange(nameof(Failed_transaction_rolls_back_the_real_table_mutation));
		using var keep = keepAlive;

		var context = provider.GetRequiredService<SqliteTxnRollbackDataContext>();
		var result = await context.ProcessSqliteTxnRollbackAsync(new SqliteTxnRollbackRequest
		{
			Widget = [new(1, "Ada"), new(2, "Alan")],
		});

		Assert.True(result.TryGetErrors(out var errors)); // error surfaced through the result
		Assert.NotEmpty(errors);
		Assert.Equal(0, CountWidgets(keepAlive)); // rolled back — the first insert is undone
	}

	/// <summary>
	/// Debug dry-run rollback STILL returns the response: with <c>@Debug</c> declared,
	/// <c>debugRollback:true</c> (the default), and <c>request.Debug == true</c> (EnvironmentName is
	/// "Production", so the debug expression reduces to <c>request.Debug</c>), the generator rolls
	/// back the real-table mutation — yet still reads and returns the response. So the response
	/// reports 2 inserted rows, but the real <c>Widgets</c> table is empty afterwards.
	/// </summary>
	[Fact]
	public async Task Debug_dry_run_rolls_back_but_still_returns_the_response()
	{
		var (keepAlive, provider) = Arrange(
			nameof(Debug_dry_run_rolls_back_but_still_returns_the_response), environmentName: "Production");
		using var keep = keepAlive;

		var context = provider.GetRequiredService<SqliteTxnDebugDataContext>();
		var result = await context.ProcessSqliteTxnDebugAsync(new SqliteTxnDebugRequest
		{
			Debug = true,
			Widget = [new(1, "Ada"), new(2, "Alan")],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(2, response!.Inserted); // response still returned (dry run)
		Assert.Equal(0, CountWidgets(keepAlive)); // but mutation rolled back
	}

	/// <summary>
	/// The complement of the dry-run: same <c>@Debug</c> query with <c>request.Debug == false</c> (and
	/// EnvironmentName "Production") commits normally — proving debugRollback rolls back ONLY when the
	/// debug expression is true, not unconditionally.
	/// </summary>
	[Fact]
	public async Task Debug_off_commits_normally()
	{
		var (keepAlive, provider) = Arrange(
			nameof(Debug_off_commits_normally), environmentName: "Production");
		using var keep = keepAlive;

		var context = provider.GetRequiredService<SqliteTxnDebugDataContext>();
		var result = await context.ProcessSqliteTxnDebugAsync(new SqliteTxnDebugRequest
		{
			Debug = false,
			Widget = [new(1, "Ada"), new(2, "Alan")],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(2, response!.Inserted);
		Assert.Equal(2, CountWidgets(keepAlive)); // committed
	}
}
