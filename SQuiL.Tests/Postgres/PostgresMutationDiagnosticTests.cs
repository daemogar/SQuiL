using Microsoft.CodeAnalysis;

using System.Linq;

using Xunit;

namespace SQuiL.Tests.Postgres;

/// <summary>
/// Task 8 (Phase 3 Postgres): SP0040 "params-before-returns" generalizes to the full
/// temp-table-header dialect family — PostgreSQL (like SQLite) must declare every
/// <c>@Param</c>/<c>@Params</c> (input) temp table before any <c>@Return</c>/<c>@Returns</c>
/// (output) temp table, because its <c>Create Temp Table</c> header creates the input tables
/// before the generated shred (<c>json_to_recordset</c>) reads them. Out-of-order is a build
/// ERROR for PostgreSQL (not merely a warning, as on SQL Server).
///
/// Mirrors <see cref="SQuiL.Tests.Ordering.OrderingDiagnosticTests"/>'s SqlServer/SQLite pair
/// (this is the PostgreSQL third case) and reuses the single-provider-inference
/// <c>RunWith&lt;Provider&gt;ProviderOnly</c> pattern from
/// <see cref="SQuiL.Tests.Sqlite.SqliteMutationDiagnosticTests"/>.
/// </summary>
public class PostgresMutationDiagnosticTests
{
	/// <summary>
	/// A PostgreSQL file declaring a <c>Param_</c>/<c>Params_</c> temp table AFTER a
	/// <c>Return_</c>/<c>Returns_</c> temp table produces SP0040 as a build ERROR — the same
	/// severity as SQLite, since both are temp-table-header dialects whose header must create
	/// every input before any output is declared.
	/// </summary>
	[Fact]
	public void Postgres_return_before_param_is_SP0040_error()
	{
		var diagnostics = TestHelper.RunWithPostgresProviderOnly(
			TestHelper.TestHeaderPostgres(["Sample"]),
			"""
			--Name: Sample
			Create Temp Table Return_A (Value int);
			Create Temp Table Param_B (Value int);
			Select 1;
			""");

		var sp = diagnostics.Where(d => d.Id == "SP0040").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
	}

	/// <summary>
	/// Sanity complement: inputs declared before outputs never fire SP0040 on PostgreSQL either.
	/// </summary>
	[Fact]
	public void Postgres_param_before_return_has_no_SP0040()
	{
		var diagnostics = TestHelper.RunWithPostgresProviderOnly(
			TestHelper.TestHeaderPostgres(["Sample"]),
			"""
			--Name: Sample
			Create Temp Table Param_B (Value int);
			Create Temp Table Return_A (Value int);
			Select 1;
			""");

		Assert.DoesNotContain(diagnostics, d => d.Id == "SP0040");
	}
}
