using Microsoft.CodeAnalysis;

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
}
