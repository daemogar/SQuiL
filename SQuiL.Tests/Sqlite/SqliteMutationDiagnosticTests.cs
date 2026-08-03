using Microsoft.CodeAnalysis;

using Xunit;

namespace SQuiL.Tests.Sqlite;

/// <summary>
/// Task 9 (Phase 3B) fold-in: the mutation scanner's SP0023 "consider [SQuiLQueryTransaction]"
/// warning must NOT fire for inserts/updates/deletes that target a query's OWN declared SQLite
/// output/param temp tables. In a SQLite query these declared tables are named bare
/// (<c>Create Temp Table Returns_Imported (...)</c>) and referenced verbatim in the body
/// (<c>Insert Into Returns_Imported ...</c>). That is the SQLite analogue of T-SQL's
/// <c>Insert Into @Return_X</c> table variable, which the scanner already recognises as a
/// NON-persistent target (the <c>@</c>-prefix skip). Before this fix the SQLite bare-name
/// insert slipped past the <c>@</c>-prefix skip and was mis-flagged as a persistent real-table
/// mutation — emitting a spurious SP0023 on every SQLite output-only round-trip query.
/// </summary>
public class SqliteMutationDiagnosticTests
{
	/// <summary>
	/// A SQLite output-only body (insert INTO a declared <c>Returns_</c> temp table, then select
	/// it back) is NOT a persistent mutation — no SP0023.
	/// </summary>
	[Fact]
	public void Insert_into_declared_output_temp_table_does_not_warn_SP0023()
	{
		var diagnostics = TestHelper.RunWithSqliteProviderOnly(
			TestHelper.BuildSource("Sample"),
			"""
			--Name: Sample
			Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT);
			Create Temp Table Returns_Imported (PersonID INTEGER Primary Key, Name TEXT);
			Insert Into Returns_Imported (PersonID, Name) Select PersonID, Name From Params_Person;
			Select PersonID, Name From Returns_Imported;
			""");

		Assert.DoesNotContain(diagnostics, d => d.Id == "SP0023");
	}

	/// <summary>
	/// An insert INTO a scalar-collapsed <c>Return_</c> temp table (single-column output) is also
	/// non-persistent — no SP0023.
	/// </summary>
	[Fact]
	public void Insert_into_declared_scalar_output_temp_table_does_not_warn_SP0023()
	{
		var diagnostics = TestHelper.RunWithSqliteProviderOnly(
			TestHelper.BuildSource("Sample"),
			"""
			--Name: Sample
			Create Temp Table Params_Counting (PersonID INTEGER Primary Key, Name TEXT);
			Create Temp Table Return_Total (Total INTEGER);
			Insert Into Return_Total (Total) Select Count(*) From Params_Counting;
			Select Total From Return_Total;
			""");

		Assert.DoesNotContain(diagnostics, d => d.Id == "SP0023");
	}

	/// <summary>
	/// SQLite-scoping guard: an insert into a table the query did NOT declare (a real, persistent
	/// table) STILL warns SP0023. This proves the fix skips only the query's own declared temp
	/// tables, not every bare-name insert.
	/// </summary>
	[Fact]
	public void Insert_into_undeclared_real_table_still_warns_SP0023()
	{
		var diagnostics = TestHelper.RunWithSqliteProviderOnly(
			TestHelper.BuildSource("Sample"),
			"""
			--Name: Sample
			Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT);
			Insert Into RealPeople (PersonID, Name) Select PersonID, Name From Params_Person;
			""");

		Assert.Contains(diagnostics, d => d.Id == "SP0023");
	}
}
