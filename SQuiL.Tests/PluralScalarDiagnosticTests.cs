using Microsoft.CodeAnalysis;

using SQuiL.SourceGenerator.Parser;

using System.Linq;

using Xunit;

namespace SQuiL.Tests;

/// <summary>
/// SP0043 — a plural direction prefix (@Params_/@Returns_) means a LIST, so the declare must
/// carry a table(...) type. A plural prefix on a scalar type used to be accepted silently and
/// then never routed at runtime: SqlServerDialect.RewriteOutputSelects builds its lookup key as
/// @Return_&lt;Name&gt;, so the plural spelling got no implicit alias, the column stayed unnamed,
/// and the result set was dropped with the response property left at its default.
/// </summary>
public class PluralScalarDiagnosticTests
{
	[Fact]
	public void Plural_output_scalar_is_SP0043_error()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Returns_Total int;\nUse [Db];\nSet @Returns_Total = 1;\nSelect @Returns_Total;"],
			includeSqlServer: true, includeSqlite: false);
		var sp = diags.Where(d => d.Id == "SP0043").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
		Assert.Contains("@Returns_Total", sp[0].GetMessage());
		Assert.Contains("@Return_Total", sp[0].GetMessage());
	}

	[Fact]
	public void Plural_input_scalar_is_SP0043_error()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Params_Limit int;\nDeclare @Return_Count int;\nUse [Db];\nSelect @Return_Count = @Params_Limit;"],
			includeSqlServer: true, includeSqlite: false);
		var sp = diags.Where(d => d.Id == "SP0043").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
		Assert.Contains("@Param_Limit", sp[0].GetMessage());
	}

	[Fact]
	public void Plural_table_declare_is_clean()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Returns_People table(PersonID int, Name varchar(50));\nUse [Db];\nInsert Into @Returns_People Select PersonID, Name From People;\nSelect * From @Returns_People;"],
			includeSqlServer: true, includeSqlite: false);
		Assert.Empty(diags.Where(d => d.Id == "SP0043"));
	}

	[Fact]
	public void Singular_scalar_declare_is_clean()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Param_Limit int;\nDeclare @Return_Count int;\nUse [Db];\nSelect @Return_Count = @Param_Limit;"],
			includeSqlServer: true, includeSqlite: false);
		Assert.Empty(diags.Where(d => d.Id == "SP0043"));
	}

	[Fact]
	public void Special_variables_are_clean()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Debug bit;\nDeclare @Return_Count int;\nUse [Db];\nSelect @Return_Count = 1;"],
			includeSqlServer: true, includeSqlite: false);
		Assert.Empty(diags.Where(d => d.Id == "SP0043"));
	}

	/// <summary>
	/// The generator's validator carries NO dialect gate — its self-exclusion on SQLite/PostgreSQL
	/// is structural. For a PLURAL single-column SQLite temp table, the exclusion happens at the
	/// <c>block.IsTable || block.IsObject</c> guard in <see cref="SQuiLPluralScalarValidator.Detect"/>:
	/// a plural single-column <c>Create Temp Table</c> never collapses to a scalar in the first
	/// place (only the SINGULAR single-column form does — see
	/// <c>SqliteHeaderTests.Returns_prefix_single_column_stays_output_list_not_scalar</c>), so this
	/// block is <c>IsTable == true</c> and the plural path never even reaches
	/// <c>LeadingVariable</c>. This test pins that end-to-end outcome (SP0043 stays silent on a real
	/// SQLite plural temp table), not the leading-`@` check inside <c>LeadingVariable</c> — that
	/// check is unreached here and, per <see cref="Sqlite_singular_scalar_temp_table_is_clean"/>'s
	/// doc comment, is a redundant early-out that no legitimate mutation can isolate.
	/// </summary>
	[Fact]
	public void Sqlite_plural_temp_table_reports_no_SP0043()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.TestHeaderSqlite(["S"])],
			["--Name: S\nCreate Temp Table Returns_Total (Total INTEGER);\nSelect Total From Returns_Total;"],
			includeSqlServer: false, includeSqlite: true);
		Assert.Empty(diags.Where(d => d.Id == "SP0043"));
	}

	/// <summary>
	/// Unlike the plural case above, a SINGULAR single-column SQLite temp table DOES collapse to a
	/// scalar (<c>CodeType.OUTPUT_VARIABLE</c>, per
	/// <c>SqliteHeaderTests.Return_prefix_single_column_collapses_to_output_scalar</c>), so
	/// <c>block.IsTable</c>/<c>IsObject</c> are both false and this is the one temp-table-dialect
	/// fixture that actually reaches <see cref="SQuiLPluralScalarValidator"/>'s
	/// <c>LeadingVariable(block.DatabaseType.Original)</c> call and its "is this null?" branch.
	///
	/// <para>
	/// It still cannot isolate the leading-`@` check inside <c>LeadingVariable</c>
	/// (<c>original![0] != '@'</c>), because that check is behaviourally redundant: the two
	/// call sites that consume its result — <c>variable.StartsWith("@Params_", …)</c> and
	/// <c>variable.StartsWith("@Returns_", …)</c> — already require a leading <c>@</c> themselves.
	/// For any bare (non-`@`) <c>Original</c> such as this fixture's <c>"Return_V INTEGER"</c>,
	/// removing just the `@` guard from <c>LeadingVariable</c> changes nothing: the method would
	/// still return the literal leading token as written (here <c>"Return_V"</c>, no `@`
	/// fabricated), which still fails both <c>StartsWith</c> checks. No input can distinguish
	/// "guard present" from "guard absent" without also changing what the method returns — and a
	/// mutation that fabricates a leading `@` that was never in the source text is not "removing
	/// this check", it is a different, unrelated bug. This test is included anyway because it
	/// pins genuine end-to-end coverage of that code path (the only temp-table-dialect scalar that
	/// reaches it), not because it can kill that one redundant guard clause.
	/// </para>
	/// </summary>
	[Fact]
	public void Sqlite_singular_scalar_temp_table_is_clean()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.TestHeaderSqlite(["S"])],
			["--Name: S\nCreate Temp Table Return_V (V INTEGER);\nInsert Into Return_V (V) Select 1;\nSelect V From Return_V;"],
			includeSqlServer: false, includeSqlite: true);
		Assert.Empty(diags.Where(d => d.Id == "SP0043"));
	}
}
