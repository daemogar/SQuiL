using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace SQuiL.Tests.Sqlite;

/// <summary>
/// Task 8 (Phase 3B): the END-TO-END INTEGRATION PROOF for the SQLite dialect. Each fact
/// compiles a real <c>[SQuiLDialect(SQuiLDialect.Sqlite)]</c> data context (generated at build
/// time from a <c>Sqlite/Queries/*.sql</c> file — the generator runs as an Analyzer on this test
/// project) and executes its generated <c>Process…Async</c> against an in-memory SQLite database,
/// asserting the response. This is where real runtime defects in Tasks 1-6 surface: param
/// binding, the <c>json_each</c> input shred (Task 6), temp-table lifetime on one connection
/// (Task 5), shape-key result routing (Task 7 invariant, proven for SQLite here), nullable-value
/// reads (Task 19), blob hex round trip (Task 6 Step 4), and error surfacing.
///
/// A pure <c>:memory:</c> database is destroyed the instant its owning connection closes, and the
/// generated <c>Process…Async</c> opens/closes its OWN connection. So every test opens a
/// KEEP-ALIVE connection to a per-test uniquely-named shared-cache memory DB
/// (<c>file:…?mode=memory&amp;cache=shared</c>) and holds it open for the test's lifetime; the
/// connection string handed to the context via <c>ConnectionStrings:SQuiLDatabase</c> is
/// byte-for-byte the same, so both connections attach to the same shared in-memory database. The
/// unique name per test keeps the facts isolated even when xUnit runs the class in parallel with
/// other classes.
///
/// Note on <c>errors</c>: on the SUCCESS path <c>SQuiLResultType.TryGetValue</c> returns
/// <c>true</c> and sets <c>errors = default</c> (null) — so success facts assert
/// <c>Assert.Null(errors)</c>, not <c>Assert.Empty</c>.
/// </summary>
public class SqliteRoundTripTests
{
	/// <summary>
	/// Opens a keep-alive connection to a per-test uniquely-named shared-cache in-memory database
	/// and returns it together with an <see cref="IServiceProvider"/> whose
	/// <c>ConnectionStrings:SQuiLDatabase</c> points at the exact same database. The caller MUST
	/// keep the returned connection alive (dispose it only after the context call completes),
	/// otherwise the shared-cache database is torn down before/after the generated
	/// <c>Process…Async</c> opens its own connection.
	/// </summary>
	private static (SqliteConnection keepAlive, ServiceProvider provider) Arrange(string dbName)
	{
		var connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared";

		var keepAlive = new SqliteConnection(connectionString);
		keepAlive.Open();

		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["ConnectionStrings:SQuiLDatabase"] = connectionString,
		}).Build();

		// The generated SQuiLExtensions.AddSQuiL() carries a static `IsLoaded` guard so a real
		// app registers exactly once. These facts each need their OWN provider (a per-test unique
		// connection string), so reset the process-wide guard before every call. Tests in one
		// xUnit class run sequentially, so there is no parallel race on this static.
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

	/// <summary>
	/// FACT 1 — list-param round trip through <c>json_each</c>. Proves the whole SQLite input
	/// marshalling path (Task 6): a <c>List&lt;Person&gt;</c> request property is serialised to a
	/// JSON TEXT parameter, shredded with <c>json_each</c>/<c>json_extract</c> into the
	/// <c>Params_Person</c> temp table, copied into <c>Returns_Imported</c>, and read back — all on
	/// the single connection <c>Process…Async</c> opens. Also the base proof that shape-key routing
	/// works for SQLite (the one <c>Returns_Imported</c> result set routes to <c>Imported</c>).
	/// </summary>
	[Fact]
	public async Task List_param_round_trips_through_json_each()
	{
		var (keepAlive, provider) = Arrange(nameof(List_param_round_trips_through_json_each));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<ImportPeopleDataContext>();
		var result = await context.ProcessImportPeopleAsync(new ImportPeopleRequest
		{
			Person = [new(1, "Ada", 36), new(2, "Alan", 41)],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(2, response!.Imported!.Count);
		Assert.Equal("Ada", response.Imported[0].Name);
		Assert.Equal(36, response.Imported[0].Age);
		Assert.Equal("Alan", response.Imported[1].Name);
		Assert.Equal(41, response.Imported[1].Age);
	}

	/// <summary>
	/// FACT 2 — null round trip (Task 19 nullable-value-type read path). A NULL <c>INTEGER</c>
	/// column declared nullable (<c>Score INTEGER null</c>) must read back as C# <c>null</c>, NOT
	/// the value type's default (<c>0</c>); likewise a NULL <c>TEXT</c> reads back as <c>null</c>.
	/// The generated reader uses <c>reader.IsDBNull(i) ? default(long?) : reader.GetInt64(i)</c>.
	/// </summary>
	[Fact]
	public async Task Null_value_column_reads_back_as_null_not_zero()
	{
		var (keepAlive, provider) = Arrange(nameof(Null_value_column_reads_back_as_null_not_zero));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<NullRoundTripDataContext>();
		var result = await context.ProcessNullRoundTripAsync(new NullRoundTripRequest());

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		var row = Assert.Single(response!.Row!);
		Assert.Equal(1, row.RowID);
		Assert.Null(row.Note);
		Assert.Null(row.Score); // must be null, not 0
	}

	/// <summary>
	/// FACT 3 — blob round trip (Task 6 Step 4: the hex converter + SQLite <c>unhex</c>). A
	/// <c>byte[]</c> travels IN through the JSON shred (serialised as uppercase hex by the shared
	/// <c>SQuiLBinaryJsonConverter</c>, decoded with <c>unhex(json_extract(…))</c>) and back OUT
	/// through <c>GetFieldValue&lt;byte[]&gt;</c>. Asserts the bytes survive byte-for-byte,
	/// including a <c>0x00</c> byte (which would truncate a naive TEXT round trip).
	/// </summary>
	[Fact]
	public async Task Blob_round_trips_byte_for_byte_through_unhex()
	{
		var (keepAlive, provider) = Arrange(nameof(Blob_round_trips_byte_for_byte_through_unhex));
		using var _ = keepAlive;

		var payload = new byte[] { 0x00, 0xAB, 0xFF, 0x10, 0x7F, 0x00, 0x42 };

		var context = provider.GetRequiredService<BlobRoundTripDataContext>();
		var result = await context.ProcessBlobRoundTripAsync(new BlobRoundTripRequest
		{
			Doc = [new(1, payload)],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		var stored = Assert.Single(response!.Stored!);
		Assert.Equal(1, stored.DocID);
		Assert.Equal(payload, stored.Payload);
	}

	/// <summary>
	/// FACT 4 — scalar return. A <c>Return_Total</c> single-column output collapses to a scalar
	/// <c>long Total</c> property on the response; it is routed by its single-column shape key
	/// (<c>total:long</c>) and read with <c>reader.GetInt64(0)</c>. Proves the input list is shredded
	/// and then aggregated server-side.
	/// </summary>
	[Fact]
	public async Task Scalar_return_reads_back_the_aggregate()
	{
		var (keepAlive, provider) = Arrange(nameof(Scalar_return_reads_back_the_aggregate));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<CountPeopleDataContext>();
		var result = await context.ProcessCountPeopleAsync(new CountPeopleRequest
		{
			Counting = [new(1, "Ada"), new(2, "Alan"), new(3, "Grace")],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(3, response!.Total);
	}

	/// <summary>
	/// FACT 5 — multi-result-set routing by shape key. The query emits TWO result sets with
	/// distinct shapes (<c>personid:long|name:string</c> and <c>total:long</c>); the reader loop
	/// walks them with <c>NextResultAsync</c> and routes each to the correct response member purely
	/// by its computed <c>ShapeKey(reader)</c> — the real proof the build-time key matches the
	/// SQLite runtime key. Both a list output and a scalar output are populated from one call.
	/// </summary>
	[Fact]
	public async Task Two_result_sets_route_by_shape_key()
	{
		var (keepAlive, provider) = Arrange(nameof(Two_result_sets_route_by_shape_key));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<PeopleAndCountDataContext>();
		var result = await context.ProcessPeopleAndCountAsync(new PeopleAndCountRequest
		{
			Roster = [new(1, "Ada"), new(2, "Alan")],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		Assert.Equal(2, response!.Echoed!.Count);
		Assert.Equal("Ada", response.Echoed[0].Name);
		Assert.Equal(2, response.Total);
	}

	/// <summary>
	/// FACT 6 — error surfacing. A SQLite runtime error (a reference to a table that does not
	/// exist) must surface through the RESULT path — <c>result.TryGetValue(out …, out errors)</c>
	/// returns <c>false</c> with a populated <c>errors</c> list — and must NOT be thrown out of
	/// <c>Process…Async</c>. The captured <see cref="SqliteException"/> is reachable via
	/// <c>SQuiLError.AsDbException()</c>, proving the provider exception was caught by the generated
	/// <c>catch(SqliteException)</c> arm and mapped by <c>SqliteDataContext.CreateError</c>.
	/// </summary>
	[Fact]
	public async Task Sqlite_error_surfaces_through_result_not_thrown()
	{
		var (keepAlive, provider) = Arrange(nameof(Sqlite_error_surfaces_through_result_not_thrown));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<MissingTableDataContext>();

		// Must NOT throw — the SqliteException is caught and returned as an error.
		var result = await context.ProcessMissingTableAsync(new MissingTableRequest());

		Assert.False(result.TryGetValue(out var ignored, out var errors));
		Assert.Null(ignored);
		Assert.NotEmpty(errors);

		var sqliteError = Assert.Single(errors, e => e.AsDbException() is SqliteException);
		Assert.Contains("NonExistentTable_XYZ", sqliteError.Message);
	}

	/// <summary>
	/// FACT 7 — routing of a result set with BOOLEAN + GUID + DATETIME columns (the Critical
	/// build-key/runtime-key parity fix). These three SQLite decltypes have INTEGER/TEXT storage-class
	/// affinities but keep their verbatim spelling in the temp table, so a live reader's
	/// <c>GetDataTypeName</c> returns <c>"BOOLEAN"</c>/<c>"GUID"</c>/<c>"DATETIME"</c>. The build-time
	/// routing key coarsens them to <c>long</c>/<c>string</c>/<c>string</c>; without the matching
	/// <c>SqliteDataContext.NormalizeType</c> entries the runtime key would be
	/// <c>boolean</c>/<c>guid</c>/<c>datetime</c>, the result set would miss the routing switch, and a
	/// spurious "Expected return table 'Flag'" error would surface even though the SQL ran fine. This
	/// fact proves the real end-to-end path: the one <c>Returns_Flag</c> result set routes to
	/// <c>Flag</c> and every typed value round-trips.
	/// </summary>
	[Fact]
	public async Task Boolean_guid_datetime_result_set_routes_and_round_trips()
	{
		var (keepAlive, provider) = Arrange(nameof(Boolean_guid_datetime_result_set_routes_and_round_trips));
		using var _ = keepAlive;

		var context = provider.GetRequiredService<TypedRoutingDataContext>();
		var result = await context.ProcessTypedRoutingAsync(new TypedRoutingRequest());

		// Pre-fix, TryGetValue returned false here (the result set fell through routing and the
		// guarded return threw "Expected return table 'Flag'"); post-fix it routes cleanly.
		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);

		var row = Assert.Single(response!.Flag!);
		Assert.Equal(1, row.FlagID);
		Assert.True(row.IsActive);
		Assert.Equal(System.Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), row.RowGuid);
		Assert.Equal(new System.DateTime(2026, 7, 27, 13, 45, 0), row.CreatedAt);
	}
}
