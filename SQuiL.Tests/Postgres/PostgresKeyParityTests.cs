namespace SQuiL.Tests.Postgres;

using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;

using Npgsql;

using SQuiL.Dialects;
using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using Xunit;

/// <summary>
/// Task 9 (Phase 3 Postgres) — the LIVE-READER counterpart of <c>KeyParityTests</c>'s
/// <c>AssertParitySqlite</c>: guards parity between the build-time shape key
/// (<c>SQuiLShapeKey.ShapeKeyOf</c> / <c>Token.CSharpType</c>) and the runtime shape key
/// (<c>PostgresDataContext.NormalizeType</c>), where the runtime provider type name comes from a
/// REAL <c>Npgsql</c> reader against a live <c>postgres:17</c> container — never a hand-fed literal.
///
/// <para>
/// This is the C1 guard: <c>PostgresDataContext.NormalizeType</c> was written in Task 1 against
/// EXPECTED Npgsql <c>GetDataTypeName</c> spellings (already unit-tested with hand-fed literals in
/// <c>PostgresCreateErrorTests</c>). This class is what actually PINS those spellings — if any real
/// Npgsql spelling differs from the map, a fact here fails and reports the true string; the fix in
/// that case is to <see cref="SQuiL.PostgresDataContext.NormalizeType"/>, never to the expectation
/// baked into this test (see the class XML doc on <c>PostgresDataContext</c>).
/// </para>
/// </summary>
[Collection("Postgres container")]
public class PostgresKeyParityTests(PostgresContainerFixture fixture)
{
	private static readonly PostgresDialect Dialect = new();

	private void SkipIfUnavailable()
	{
		if (fixture.StartupFailure is { } ex)
			Assert.Skip($"No container runtime available for the PostgreSQL live-reader parity tests: {ex.Message}");
	}

	// Minimal concrete context so the protected, virtually-dispatched NormalizeType override can be
	// reached from a test (mirrors KeyParityTests.SqlServerProbe/SqliteProbe).
	private sealed class PostgresProbe() : PostgresDataContext(new ConfigurationBuilder().Build());

	/// <summary>
	/// Parses a single-column <c>Create Temp Table</c> declare under <see cref="PostgresDialect"/>,
	/// extracts the build-time canonical routing token via the dialect-aware
	/// <c>SQuiLShapeKey.ShapeKeyOf</c> overload, creates a REAL temp table of the exact decltype the
	/// generator would emit (<c>Token.Original</c>, verbatim — see
	/// <c>PostgresDialect.TableVariableDeclaration</c>) against the live container, reads what a real
	/// <see cref="NpgsqlDataReader.GetDataTypeName"/> reports, and asserts
	/// <c>PostgresDataContext.NormalizeType</c> of that LIVE string equals the build-time token.
	/// </summary>
	private async Task AssertParityPostgresAsync(string sqlType)
	{
		var tokens = SQuiLTokenizer.GetTokens($"Create Temp Table Returns_T (C {sqlType});\nSelect 1;", Dialect);
		var blocks = SQuiLParser.ParseTokens(tokens, Dialect);
		var block = blocks.Find(b => b.IsTable || b.IsObject);
		Assert.NotNull(block);

		var shapeKey = SQuiLShapeKey.ShapeKeyOf(block!, Dialect);
		// Key is "c:<token>" — take the part after the colon.
		var colonIdx = shapeKey.IndexOf(':');
		Assert.True(colonIdx >= 0, $"ShapeKeyOf returned unexpected format: '{shapeKey}'");
		var buildToken = shapeKey.Substring(colonIdx + 1);

		// Faithful runtime observation: create a column with the SAME decltype the generator emits
		// (Token.Original, which TableVariableDeclaration writes verbatim), select it against the
		// LIVE container, and read what Npgsql actually reports for GetDataTypeName — the real
		// routing input SQuiLBaseDataContext.ShapeKey observes at runtime.
		var emittedDeclType = block!.Properties[0].Type.Original!;
		var providerTypeName = await ReadProviderTypeNameAsync(emittedDeclType);

		var runtimeToken = new PostgresProbe().NormalizeTypeForTest(providerTypeName);

		Assert.Equal(buildToken, runtimeToken);
	}

	/// <summary>
	/// Creates a temp table with a single column of <paramref name="declType"/> against the live
	/// container, selects it, and returns the live <c>DbDataReader.GetDataTypeName(0)</c> — the exact
	/// provider type name the runtime router (<c>SQuiLBaseDataContext.ShapeKey</c>) observes for such
	/// a column. Uses its own connection (PostgreSQL temp tables are session-scoped) so parallel/serial
	/// facts never collide on the probe table name.
	/// </summary>
	private async Task<string> ReadProviderTypeNameAsync(string declType)
	{
		await using var connection = new NpgsqlConnection(fixture.ConnectionString);
		await connection.OpenAsync();

		await using (var create = connection.CreateCommand())
		{
			create.CommandText = $"Create Temp Table __parity_probe (c {declType});";
			await create.ExecuteNonQueryAsync();
		}

		await using var select = connection.CreateCommand();
		select.CommandText = "Select c From __parity_probe;";
		await using var reader = await select.ExecuteReaderAsync();
		Assert.True(await reader.ReadAsync() || reader.FieldCount == 1);
		return reader.GetDataTypeName(0);
	}

	// Every PG type the brief calls out: int2/4/8, text/varchar, bytea, uuid, bool, timestamp,
	// timestamptz, date, time, numeric, real, double precision, money, json/jsonb. Build RoutingType
	// must equal runtime NormalizeType(GetDataTypeName) for each — no fictional inputs, every provider
	// type name comes from the live container.

	[Fact] public Task Pg_Parity_Int2() => RunAsync("int2");
	[Fact] public Task Pg_Parity_Int4() => RunAsync("int4");
	[Fact] public Task Pg_Parity_Int8() => RunAsync("int8");
	[Fact] public Task Pg_Parity_Text() => RunAsync("text");
	[Fact] public Task Pg_Parity_Varchar() => RunAsync("varchar(100)");
	[Fact] public Task Pg_Parity_Bytea() => RunAsync("bytea");
	[Fact] public Task Pg_Parity_Uuid() => RunAsync("uuid");
	[Fact] public Task Pg_Parity_Boolean() => RunAsync("boolean");
	[Fact] public Task Pg_Parity_Timestamp() => RunAsync("timestamp");
	[Fact] public Task Pg_Parity_Timestamptz() => RunAsync("timestamptz");
	[Fact] public Task Pg_Parity_Date() => RunAsync("date");
	[Fact] public Task Pg_Parity_Time() => RunAsync("time");
	[Fact] public Task Pg_Parity_Numeric() => RunAsync("numeric(18,2)");
	[Fact] public Task Pg_Parity_Real() => RunAsync("real");
	[Fact] public Task Pg_Parity_DoublePrecision() => RunAsync("double precision");
	[Fact] public Task Pg_Parity_Money() => RunAsync("money");
	[Fact] public Task Pg_Parity_Json() => RunAsync("json");
	[Fact] public Task Pg_Parity_Jsonb() => RunAsync("jsonb");

	private async Task RunAsync(string sqlType)
	{
		SkipIfUnavailable();
		await AssertParityPostgresAsync(sqlType);
	}
}
