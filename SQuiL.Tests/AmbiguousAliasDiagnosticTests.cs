using Microsoft.CodeAnalysis;

using System.Linq;

using Xunit;

namespace SQuiL.Tests;

/// <summary>
/// SP0044 — `Select @Return_Count Throw;` is valid T-SQL (an AS-less column alias), but `throw`
/// and `go` are also statement starters and are the only NON-RESERVED members of that set. The
/// scanner cannot tell the two readings apart, and guessing "statement" made it append
/// `As [Count]`, emitting SQL that does not parse. Now an error asking the author to disambiguate.
/// </summary>
public class AmbiguousAliasDiagnosticTests
{
	[Fact]
	public void Throw_after_a_scalar_select_is_SP0044_error()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Return_Count int;\nUse [Db];\nSet @Return_Count = 1;\nSelect @Return_Count Throw;"],
			includeSqlServer: true, includeSqlite: false);
		var sp = diags.Where(d => d.Id == "SP0044").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
		// The `--Name: S` header comment is stripped before the SQL reaches the validator (see
		// AdditionalQuery.GetText), so line counting starts at the `Declare` line — the same
		// convention MultiScalarSelectDiagnosticTests documents for SP0041: 1: Declare, 2: Use,
		// 3: Set, 4: Select.
		Assert.Contains("line 4", sp[0].GetMessage());
		// The message must NOT advise writing `As [Throw]`. SQuiL routes a scalar result set by the
		// DECLARED name — SQuiLShapeKey.ScalarKeyOf keys on block.Name — while the RUNTIME shape key
		// is built from reader.GetName(0), i.e. the written alias. The generated switch has no
		// `default:` arm, so an alias that differs from the declared name builds clean and then
		// silently drops the result set: precisely the failure SP0043/SP0044 exist to prevent.
		Assert.DoesNotContain("As [Throw]", sp[0].GetMessage());
		// It offers the two remedies that actually work instead: terminate the Select, or rename
		// the declare so the column name the author wants IS the declared name.
		Assert.Contains("@Return_Throw", sp[0].GetMessage());
		// The message names the offending scalar as the author spelled it in the SQL, so a file
		// with several output scalars does not force a line-count to work out which one is meant.
		Assert.Contains("@Return_Count", sp[0].GetMessage());
	}

	[Fact]
	public void Go_after_a_scalar_select_is_SP0044_error()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Return_Count int;\nUse [Db];\nSet @Return_Count = 1;\nSelect @Return_Count go;"],
			includeSqlServer: true, includeSqlite: false);
		Assert.Single(diags.Where(d => d.Id == "SP0044"));
	}

	[Fact]
	public void An_ordinary_bare_scalar_select_is_clean()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Return_Count int;\nUse [Db];\nSet @Return_Count = 1;\nSelect @Return_Count;"],
			includeSqlServer: true, includeSqlite: false);
		Assert.Empty(diags.Where(d => d.Id == "SP0044"));
	}

	[Fact]
	public void An_explicitly_aliased_select_is_clean()
	{
		var diags = TestHelper.RunForDiagnostics(
			[TestHelper.BuildSource("S")],
			["--Name: S\nDeclare @Return_Count int;\nUse [Db];\nSet @Return_Count = 1;\nSelect @Return_Count As [Total];"],
			includeSqlServer: true, includeSqlite: false);
		Assert.Empty(diags.Where(d => d.Id == "SP0044"));
	}
}
