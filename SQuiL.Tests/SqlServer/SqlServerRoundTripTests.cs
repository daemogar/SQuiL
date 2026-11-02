using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL;

using Xunit;

namespace SQuiL.Tests.SqlServer;

/// <summary>
/// End-to-end tests for the SQL Server dialect against a real server, the SQL Server twin of
/// <c>PostgresRoundTripTests</c> and <c>SqliteRoundTripTests</c>. Each test calls a real compiled
/// <c>[SQuiLDialect(SQuiLDialect.SqlServer)]</c> data context (generated at build time from a
/// <c>SqlServer/Queries/*.sql</c> file) through <c>AddSQuiL()</c>, against the server from
/// <see cref="SqlServerContainerFixture"/>. They cover what snapshot tests cannot: parameter
/// binding, the <c>OpenJson</c> input shred, table-variable round trips, shape-key result routing,
/// the implicit scalar alias, nullable reads, the hex blob round trip and error surfacing.
///
/// <para>
/// Every test first checks <see cref="SqlServerContainerFixture.StartupFailure"/> and skips when no
/// server is available.
/// </para>
///
/// Success-path tests unwrap the result with <see cref="Ok"/>, which fails with SQuiL's error
/// messages when there are any.
/// </summary>
[Collection("SqlServer container")]
public class SqlServerRoundTripTests(SqlServerContainerFixture fixture)
{
	private ServiceProvider Arrange(string? connectionString = null)
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["ConnectionStrings:SQuiLDatabase"] = connectionString ?? fixture.ConnectionString,
		}).Build();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(config);
		services.AddSQuiL();
		return services.BuildServiceProvider();
	}

	/// <summary>
	/// Returns the response, or fails with the errors SQuiL returned (the server's own messages), so a
	/// failure shows why instead of just "expected True".
	/// </summary>
	private static T Ok<T>(SQuiLResultType<T> result)
	{
		if (!result.TryGetValue(out var value, out var errors))
			Assert.Fail("SQuiL returned errors: " + string.Join(" | ", errors.Select(e => e.Message)));
		return value;
	}

	private void SkipIfUnavailable()
	{
		if (fixture.StartupFailure is { } ex)
			Assert.Skip($"No SQL Server available for the SQL Server round-trip tests: {ex.Message}");
	}

	/// <summary>
	/// All three input shapes: a list param round trips through <c>OpenJson</c> into a returned
	/// list; a list param feeds a scalar aggregate (returned through the implicit
	/// <c>As [Total]</c> alias); and an object param round trips into a returned object of the same
	/// name (cross-side same-name sharing).
	/// </summary>
	[Fact]
	public async Task Param_fidelity_list_scalar_and_object_round_trip()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		var peopleContext = provider.GetRequiredService<MsImportPeopleDataContext>();
		var peopleResult = await peopleContext.ProcessMsImportPeopleAsync(new MsImportPeopleRequest
		{
			MsPerson = [new(1, "Ada", 36), new(2, "Alan", 41)],
		}, TestContext.Current.CancellationToken);

		var peopleResponse = Ok(peopleResult);
		Assert.Equal(2, peopleResponse!.MsImported!.Count);
		Assert.Equal("Ada", peopleResponse.MsImported[0].Name);
		Assert.Equal(36, peopleResponse.MsImported[0].Age);
		Assert.Equal("Alan", peopleResponse.MsImported[1].Name);
		Assert.Equal(41, peopleResponse.MsImported[1].Age);

		var countContext = provider.GetRequiredService<MsCountPeopleDataContext>();
		var countResult = await countContext.ProcessMsCountPeopleAsync(new MsCountPeopleRequest
		{
			MsCounting = [new(1, "Ada"), new(2, "Alan"), new(3, "Grace")],
		}, TestContext.Current.CancellationToken);

		var countResponse = Ok(countResult);
		Assert.Equal(3, countResponse!.Total);

		var addressContext = provider.GetRequiredService<MsImportAddressDataContext>();
		var addressResult = await addressContext.ProcessMsImportAddressAsync(new MsImportAddressRequest
		{
			MsAddress = new("123 Main St", "Chattanooga"),
		}, TestContext.Current.CancellationToken);

		var addressResponse = Ok(addressResult);
		Assert.Equal("123 Main St", addressResponse!.MsAddress!.Street);
		Assert.Equal("Chattanooga", addressResponse.MsAddress.City);
	}

	/// <summary>
	/// An <c>nvarchar</c> parameter keeps characters outside the database collation's code page.
	/// Before #97 every string parameter was sent as <c>varchar</c>, so this came back as
	/// <c>Lodz ?</c>, with no error.
	/// </summary>
	[Fact]
	public async Task Nvarchar_param_keeps_unicode_characters()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		const string text = "Łódź 中文 😀";

		var context = provider.GetRequiredService<MsUnicodeRoundTripDataContext>();
		var result = await context.ProcessMsUnicodeRoundTripAsync(new MsUnicodeRoundTripRequest
		{
			Text = text,
		}, TestContext.Current.CancellationToken);

		var response = Ok(result);
		Assert.Equal(text, response!.Text);
	}

	/// <summary>A NULL nullable <c>int</c> reads back as C# <c>null</c>, not <c>0</c>; a NULL <c>varchar</c> as <c>null</c>.</summary>
	[Fact]
	public async Task Null_value_column_reads_back_as_null_not_zero()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		var context = provider.GetRequiredService<MsNullRoundTripDataContext>();
		var result = await context.ProcessMsNullRoundTripAsync(new MsNullRoundTripRequest(), TestContext.Current.CancellationToken);

		var response = Ok(result);
		var row = Assert.Single(response!.MsRow!);
		Assert.Equal(1, row.RowID);
		Assert.Null(row.Note);
		Assert.Null(row.Score);
	}

	/// <summary>
	/// A <c>byte[]</c> goes in through the JSON shred (hex, decoded with
	/// <c>Convert(varbinary(max), …, 2)</c>) and comes back out of a real <c>varbinary(max)</c>
	/// column byte for byte, including <c>0x00</c> bytes.
	/// </summary>
	[Fact]
	public async Task Blob_round_trips_byte_for_byte_through_hex()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		var payload = new byte[] { 0x00, 0xAB, 0xFF, 0x10, 0x7F, 0x00, 0x42 };

		var context = provider.GetRequiredService<MsBlobRoundTripDataContext>();
		var result = await context.ProcessMsBlobRoundTripAsync(new MsBlobRoundTripRequest
		{
			MsDoc = [new(1, payload)],
		}, TestContext.Current.CancellationToken);

		var response = Ok(result);
		var stored = Assert.Single(response!.MsStored!);
		Assert.Equal(1, stored.DocID);
		Assert.Equal(payload, stored.Payload);
	}

	/// <summary>
	/// Result sets route to the right response members by shape key alone: (a) a list and a scalar
	/// from one call; (b) one result set carrying <c>bit</c>, <c>uniqueidentifier</c>,
	/// <c>datetime2</c>, <c>decimal</c> and <c>datetimeoffset</c> columns, with every value read back.
	/// </summary>
	[Fact]
	public async Task Result_sets_route_by_shape_key_including_typed_columns()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		var peopleAndCountContext = provider.GetRequiredService<MsPeopleAndCountDataContext>();
		var peopleAndCountResult = await peopleAndCountContext.ProcessMsPeopleAndCountAsync(new MsPeopleAndCountRequest
		{
			MsRoster = [new(1, "Ada"), new(2, "Alan")],
		}, TestContext.Current.CancellationToken);

		var peopleAndCountResponse = Ok(peopleAndCountResult);
		Assert.Equal(2, peopleAndCountResponse!.MsEchoed!.Count);
		Assert.Equal("Ada", peopleAndCountResponse.MsEchoed[0].Name);
		Assert.Equal(2, peopleAndCountResponse.Total);

		var typedRoutingContext = provider.GetRequiredService<MsTypedRoutingDataContext>();
		var typedRoutingResult = await typedRoutingContext.ProcessMsTypedRoutingAsync(new MsTypedRoutingRequest(), TestContext.Current.CancellationToken);

		var typedRoutingResponse = Ok(typedRoutingResult);

		var row = Assert.Single(typedRoutingResponse!.MsFlag!);
		Assert.Equal(1, row.FlagID);
		Assert.True(row.IsActive);
		Assert.Equal(System.Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), row.RowGuid);
		Assert.Equal(new System.DateTime(2026, 7, 27, 13, 45, 0), row.CreatedAt);
		Assert.Equal(12.34m, row.Amount);
		Assert.Equal(new System.DateTimeOffset(2026, 7, 27, 13, 45, 0, System.TimeSpan.FromHours(-5)), row.Stamp);
	}

	/// <summary>
	/// The generated query's <c>Use</c> always names the connection string's database, never the one
	/// the query file names: <c>MsCurrentDatabase.sql</c> says <c>Use [tempdb]</c>, yet it runs in
	/// whatever database the connection string names.
	/// </summary>
	[Theory]
	[InlineData("master")]
	[InlineData("model")]
	[InlineData("tempdb")]
	public async Task Query_runs_in_the_connection_string_database_not_its_own_Use(string database)
	{
		SkipIfUnavailable();
		using var provider = Arrange(new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database }.ConnectionString);

		var context = provider.GetRequiredService<MsCurrentDatabaseDataContext>();
		var result = await context.ProcessMsCurrentDatabaseAsync(new MsCurrentDatabaseRequest(), TestContext.Current.CancellationToken);

		Assert.Equal(database, Ok(result)!.Database);
	}

	/// <summary>
	/// A connection string that names no database returns a clear error and never reaches the server.
	/// Before this, the generated query sent <c>Use [];</c>, which failed with "Database '' does not
	/// exist". The server isn't needed for this, so the test doesn't skip without one.
	/// </summary>
	[Fact]
	public async Task Connection_string_without_a_database_returns_a_clear_error()
	{
		var builder = new SqlConnectionStringBuilder("Server=127.0.0.1,1;User ID=sa;Password=unused;Connect Timeout=1");
		using var provider = Arrange(builder.ConnectionString);

		var context = provider.GetRequiredService<MsCurrentDatabaseDataContext>();
		var result = await context.ProcessMsCurrentDatabaseAsync(new MsCurrentDatabaseRequest(), TestContext.Current.CancellationToken);

		Assert.False(result.TryGetValue(out _, out var errors));
		var error = Assert.Single(errors);
		Assert.Equal(51002, error.Number);
		Assert.Contains("`SQuiLDatabase` names no database", error.Message);
	}

	/// <summary>
	/// A real SQL Server error (a missing table) comes back through the result, not as a thrown
	/// exception, with the <see cref="SqlException"/> reachable through <c>AsDbException()</c>.
	/// </summary>
	[Fact]
	public async Task SqlServer_error_surfaces_through_result_not_thrown()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		var context = provider.GetRequiredService<MsMissingTableDataContext>();
		var result = await context.ProcessMsMissingTableAsync(new MsMissingTableRequest(), TestContext.Current.CancellationToken);

		Assert.False(result.TryGetValue(out var ignored, out var errors));
		Assert.Null(ignored);
		Assert.NotEmpty(errors);

		var sqlError = Assert.Single(errors, e => e.AsDbException() is SqlException);
		Assert.Contains("NonExistentTable_XYZ", sqlError.Message, System.StringComparison.OrdinalIgnoreCase);
	}
}
