using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace SQuiL.Tests.Postgres;

/// <summary>
/// Task 9 (Phase 3 Postgres): the END-TO-END INTEGRATION PROOF for the PostgreSQL dialect, the live
/// twin of <c>SqliteRoundTripTests</c>. Each fact compiles a real
/// <c>[SQuiLDialect(SQuiLDialect.Postgres)]</c> data context (generated at build time from a
/// <c>Postgres/Queries/*.sql</c> file — the generator runs as an Analyzer on this test project) and
/// executes its generated <c>Process…Async</c> against a REAL <c>postgres:17</c> container started by
/// <see cref="PostgresContainerFixture"/>, asserting the response. This is where real runtime defects
/// surface: param binding, the <c>json_to_recordset</c> input shred (Task 6), temp-table lifetime on
/// one connection (Task 5), shape-key result routing (Task 7 invariant, proven for PostgreSQL here),
/// nullable-value reads (Task 19), the <c>decode(…, 'hex')</c> blob round trip (Task 6 Step 4), and
/// error surfacing.
///
/// <para>
/// Unlike the SQLite twin, PostgreSQL's temp tables are session-scoped to a REAL server-side
/// connection — no shared-cache keep-alive trick is needed. Each generated
/// <c>Process…Async</c> opens its own <see cref="NpgsqlConnection"/> and runs the whole header +
/// shred + body as one command batch on it, so a plain connection string to the live container is
/// sufficient.
/// </para>
///
/// <para>
/// Every fact first checks <see cref="PostgresContainerFixture.StartupFailure"/> and skips
/// (<c>Assert.Skip</c>) when no container runtime (Docker/Podman) is reachable, rather than failing
/// the whole suite on a machine with neither installed — see the fixture's XML doc. On this repo's
/// dev machine, Docker is up, so these facts actually run.
/// </para>
///
/// Note on <c>errors</c>: on the SUCCESS path <c>SQuiLResultType.TryGetValue</c> returns
/// <c>true</c> and sets <c>errors = default</c> (null) — so success facts assert
/// <c>Assert.Null(errors)</c>, not <c>Assert.Empty</c>.
/// </summary>
[Collection("Postgres container")]
public class PostgresRoundTripTests(PostgresContainerFixture fixture)
{
	private ServiceProvider Arrange()
	{
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["ConnectionStrings:SQuiLDatabase"] = fixture.ConnectionString,
		}).Build();

		// The generated SQuiLExtensions.AddSQuiL() carries a static `IsLoaded` guard so a real
		// app registers exactly once. These facts each need their OWN provider, so reset the
		// process-wide guard before every call. Facts in this collection run sequentially (xUnit
		// serialises every class sharing one collection fixture), so there is no parallel race.
		ResetAddSQuiLGuard();

		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(config);
		services.AddSQuiL();
		return services.BuildServiceProvider();
	}

	private static void ResetAddSQuiLGuard()
	{
		var property = typeof(SQuiLExtensions).GetProperty(
			nameof(SQuiLExtensions.IsLoaded),
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
		property!.SetValue(null, false);
	}

	/// <summary>
	/// Skips the calling fact when <see cref="PostgresContainerFixture.StartupFailure"/> is set
	/// (no Docker/Podman runtime reachable) instead of failing outright.
	/// </summary>
	private void SkipIfUnavailable()
	{
		if (fixture.StartupFailure is { } ex)
			Assert.Skip($"No container runtime available for the PostgreSQL round-trip tests: {ex.Message}");
	}

	/// <summary>
	/// FACT 1 — param fidelity. Covers all three input cardinalities in one fact: a
	/// <c>List&lt;PgPerson&gt;</c> list param round trips through <c>json_to_recordset</c> into
	/// <c>Returns_PgImported</c> and back (proving the whole PostgreSQL input marshalling path, Task
	/// 6); a scalar aggregate (<c>Count(*)</c> over a shredded list param) reads back correctly; and a
	/// single object param (<c>Param_Address</c>) round trips into <c>Return_Address</c> and back —
	/// the SAME <c>Address</c> record shared cross-side within one file (input object + output
	/// object, the CLAUDE.md same-name-merge rule).
	/// </summary>
	[Fact]
	public async Task Param_fidelity_list_scalar_and_object_round_trip()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		// List param -> list return.
		var peopleContext = provider.GetRequiredService<PgImportPeopleDataContext>();
		var peopleResult = await peopleContext.ProcessPgImportPeopleAsync(new PgImportPeopleRequest
		{
			PgPerson = [new(1, "Ada", 36), new(2, "Alan", 41)],
		});

		Assert.True(peopleResult.TryGetValue(out var peopleResponse, out var peopleErrors));
		Assert.Null(peopleErrors);
		Assert.Equal(2, peopleResponse!.PgImported!.Count);
		Assert.Equal("Ada", peopleResponse.PgImported[0].Name);
		Assert.Equal(36, peopleResponse.PgImported[0].Age);
		Assert.Equal("Alan", peopleResponse.PgImported[1].Name);
		Assert.Equal(41, peopleResponse.PgImported[1].Age);

		// List param -> scalar aggregate return.
		var countContext = provider.GetRequiredService<PgCountPeopleDataContext>();
		var countResult = await countContext.ProcessPgCountPeopleAsync(new PgCountPeopleRequest
		{
			PgCounting = [new(1, "Ada"), new(2, "Alan"), new(3, "Grace")],
		});

		Assert.True(countResult.TryGetValue(out var countResponse, out var countErrors));
		Assert.Null(countErrors);
		Assert.Equal(3, countResponse!.Total);

		// Object param -> object return (shared `Address` record, input + output side).
		var addressContext = provider.GetRequiredService<PgImportAddressDataContext>();
		var addressResult = await addressContext.ProcessPgImportAddressAsync(new PgImportAddressRequest
		{
			Address = new("123 Main St", "Chattanooga"),
		});

		Assert.True(addressResult.TryGetValue(out var addressResponse, out var addressErrors));
		Assert.Null(addressErrors);
		Assert.Equal("123 Main St", addressResponse!.Address!.Street);
		Assert.Equal("Chattanooga", addressResponse.Address.City);
	}

	/// <summary>
	/// FACT 2 — null fidelity (Task 19 nullable-value-type read path). A NULL <c>int4</c> column
	/// declared nullable (<c>Score int4 null</c>) must read back as C# <c>null</c>, NOT the value
	/// type's default (<c>0</c>); likewise a NULL <c>text</c> reads back as <c>null</c>.
	/// </summary>
	[Fact]
	public async Task Null_value_column_reads_back_as_null_not_zero()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		var context = provider.GetRequiredService<PgNullRoundTripDataContext>();
		var result = await context.ProcessPgNullRoundTripAsync(new PgNullRoundTripRequest());

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		var row = Assert.Single(response!.PgRow!);
		Assert.Equal(1, row.RowID);
		Assert.Null(row.Note);
		Assert.Null(row.Score); // must be null, not 0
	}

	/// <summary>
	/// FACT 3 — blob fidelity (Task 6 Step 4: the hex converter + PostgreSQL <c>decode(…, 'hex')</c>).
	/// A <c>byte[]</c> travels IN through the JSON shred (serialised as uppercase hex by the shared
	/// <c>SQuiLBinaryJsonConverter</c>, decoded with <c>decode(x."Payload", 'hex')</c>) and back OUT
	/// through <c>GetFieldValue&lt;byte[]&gt;</c> reading a real <c>bytea</c> column. Asserts the bytes
	/// survive byte-for-byte, including a <c>0x00</c> byte.
	/// </summary>
	[Fact]
	public async Task Blob_round_trips_byte_for_byte_through_decode_hex()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		var payload = new byte[] { 0x00, 0xAB, 0xFF, 0x10, 0x7F, 0x00, 0x42 };

		var context = provider.GetRequiredService<PgBlobRoundTripDataContext>();
		var result = await context.ProcessPgBlobRoundTripAsync(new PgBlobRoundTripRequest
		{
			PgDoc = [new(1, payload)],
		});

		Assert.True(result.TryGetValue(out var response, out var errors));
		Assert.Null(errors);
		var stored = Assert.Single(response!.PgStored!);
		Assert.Equal(1, stored.DocID);
		Assert.Equal(payload, stored.Payload);
	}

	/// <summary>
	/// FACT 4 — result-set routing by shape key, in two parts. Part (a): two DISTINCT result-set
	/// shapes (<c>personid:int|name:string</c> and <c>total:long</c>) from one call route to the
	/// correct response members purely by <c>ShapeKey(reader)</c> — the real proof the build-time key
	/// matches the PostgreSQL runtime key. Part (b), the C1 guard: a result set whose columns include
	/// <c>uuid</c>/<c>boolean</c>/<c>timestamp</c> — the case-sensitivity-risk types under Option-B
	/// identifier folding — routes and round-trips every typed value correctly.
	/// </summary>
	[Fact]
	public async Task Result_sets_route_by_shape_key_including_uuid_boolean_timestamp()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		// (a) two shapes from one call.
		var peopleAndCountContext = provider.GetRequiredService<PgPeopleAndCountDataContext>();
		var peopleAndCountResult = await peopleAndCountContext.ProcessPgPeopleAndCountAsync(new PgPeopleAndCountRequest
		{
			PgRoster = [new(1, "Ada"), new(2, "Alan")],
		});

		Assert.True(peopleAndCountResult.TryGetValue(out var peopleAndCountResponse, out var peopleAndCountErrors));
		Assert.Null(peopleAndCountErrors);
		Assert.Equal(2, peopleAndCountResponse!.PgEchoed!.Count);
		Assert.Equal("Ada", peopleAndCountResponse.PgEchoed[0].Name);
		Assert.Equal(2, peopleAndCountResponse.Total);

		// (b) uuid + boolean + timestamp columns in one result set.
		var typedRoutingContext = provider.GetRequiredService<PgTypedRoutingDataContext>();
		var typedRoutingResult = await typedRoutingContext.ProcessPgTypedRoutingAsync(new PgTypedRoutingRequest());

		Assert.True(typedRoutingResult.TryGetValue(out var typedRoutingResponse, out var typedRoutingErrors));
		Assert.Null(typedRoutingErrors);

		var row = Assert.Single(typedRoutingResponse!.PgFlag!);
		Assert.Equal(1, row.FlagID);
		Assert.True(row.IsActive);
		Assert.Equal(System.Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), row.RowGuid);
		Assert.Equal(new System.DateTime(2026, 7, 27, 13, 45, 0), row.CreatedAt);
	}

	/// <summary>
	/// FACT 5 — error surfacing. A real PostgreSQL runtime error (a reference to a table that does not
	/// exist) must surface through the RESULT path — <c>result.TryGetValue(out …, out errors)</c>
	/// returns <c>false</c> with a populated <c>errors</c> list — and must NOT be thrown out of
	/// <c>Process…Async</c>. The captured <see cref="NpgsqlException"/> is reachable via
	/// <c>SQuiLError.AsDbException()</c>, proving the provider exception was caught by the generated
	/// <c>catch(NpgsqlException)</c> arm and mapped by <c>PostgresDataContext.CreateError</c>.
	/// </summary>
	[Fact]
	public async Task Postgres_error_surfaces_through_result_not_thrown()
	{
		SkipIfUnavailable();
		using var provider = Arrange();

		var context = provider.GetRequiredService<PgMissingTableDataContext>();

		// Must NOT throw — the NpgsqlException is caught and returned as an error.
		var result = await context.ProcessPgMissingTableAsync(new PgMissingTableRequest());

		Assert.False(result.TryGetValue(out var ignored, out var errors));
		Assert.Null(ignored);
		Assert.NotEmpty(errors);

		var pgError = Assert.Single(errors, e => e.AsDbException() is NpgsqlException);
		Assert.Contains("nonexistenttable_xyz", pgError.Message, System.StringComparison.OrdinalIgnoreCase);
	}
}
