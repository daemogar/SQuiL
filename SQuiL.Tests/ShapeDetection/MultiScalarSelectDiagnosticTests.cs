using Microsoft.CodeAnalysis;

using System.Linq;

using Xunit;

namespace SQuiL.Tests.ShapeDetection;

/// <summary>
/// SP0041 — a `Select` listing more than one output scalar cannot be routed to a response. The
/// runtime shape key for `Select @Return_A, @Return_B` is "a:int|b:int", while the generated cases
/// are per-scalar single-column keys, so nothing matches and the result set is silently skipped.
/// Aliasing does not help. Always an Error, on every dialect.
/// </summary>
public class MultiScalarSelectDiagnosticTests
{
	[Fact]
	public void Two_scalars_in_one_select_is_SP0041_error()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.BuildSource("S")], ["--Name: S\nDeclare @Return_A int;\nDeclare @Return_B int;\nUse [Db];\nSet @Return_A = 1;\nSet @Return_B = 2;\nSelect @Return_A, @Return_B;"], includeSqlServer: true, includeSqlite: false);
		var sp = diags.Where(d => d.Id == "SP0041").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
		// The diagnostic's Location is Location.None (AdditionalText SQL files carry no Roslyn
		// Location), so the reported line is only observable inside the message text. The `--Name:
		// S` header comment is stripped before the SQL reaches the validator, so line counting
		// starts at the `Declare` line: 1-2: Declare, 3: Use, 4-5: Set, 6: Select.
		Assert.Contains("line 6", sp[0].GetMessage());
	}

	/// <summary>
	/// Regression for the review finding that <c>Detect</c>'s old <c>scalars.Count &lt; 2</c> guard
	/// conflated "distinct declared scalars" with "column-list entries": a file declaring only ONE
	/// output scalar can still trip SP0041 by referencing that single scalar twice in one select.
	/// The runtime shape key for <c>Select @Return_A, @Return_A</c> is <c>"a:int|a:int"</c> — a
	/// genuine two-column key matching no generated single-column case, exactly the failure SP0041
	/// exists to catch.
	/// </summary>
	[Fact]
	public void Single_declared_scalar_referenced_twice_is_SP0041_error()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.BuildSource("S")], ["--Name: S\nDeclare @Return_A int;\nUse [Db];\nSet @Return_A = 1;\nSelect @Return_A, @Return_A;"], includeSqlServer: true, includeSqlite: false);
		var sp = diags.Where(d => d.Id == "SP0041").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
	}

	/// <summary>Aliasing does not rescue a multi-scalar select — the key is still two columns.</summary>
	[Fact]
	public void Two_aliased_scalars_in_one_select_is_SP0041_error()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.BuildSource("S")], ["--Name: S\nDeclare @Return_A int;\nDeclare @Return_B int;\nUse [Db];\nSet @Return_A = 1;\nSet @Return_B = 2;\nSelect @Return_A As A, @Return_B As B;"], includeSqlServer: true, includeSqlite: false);
		var sp = diags.Where(d => d.Id == "SP0041").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
	}

	[Fact]
	public void One_scalar_per_select_is_clean()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.BuildSource("S")], ["--Name: S\nDeclare @Return_A int;\nDeclare @Return_B int;\nUse [Db];\nSet @Return_A = 1;\nSet @Return_B = 2;\nSelect @Return_A;\nSelect @Return_B;"], includeSqlServer: true, includeSqlite: false);
		Assert.Empty(diags.Where(d => d.Id == "SP0041"));
	}

	/// <summary>
	/// A scalar assignment is not a result set at all, so it never trips SP0041 — this test asserts
	/// exactly that (a legitimate no-false-positive guard) and no more.
	///
	/// <para>
	/// <b>What this test does NOT prove:</b> it does not (and structurally cannot) exercise
	/// <see cref="ScalarSelectAliaser"/>'s <c>ParseColumnList</c> assignment-form exclusion (the
	/// logic that rejects <c>Select @X = …</c> as not a clean column list). <c>Select @Return_A =
	/// Count(*) From People</c> accumulates only ONE bare-scalar column entry before hitting <c>=</c>,
	/// and <c>FindMultiScalarSelects</c> requires <c>columns.Count &gt;= 2</c> before it looks at
	/// anything — so this test passes whether or not the assignment-form exclusion works. There is
	/// no legitimate (valid-T-SQL) way to get a second bare scalar into an assignment-form select to
	/// close this gap: SQL Server rejects combining a variable assignment with data retrieval in one
	/// SELECT, so a fixture like <c>Select @Return_A = Count(*), @Return_A From People</c> is invalid
	/// syntax, not a real test case.
	/// </para>
	///
	/// <para>
	/// The assignment-form exclusion IS genuinely covered elsewhere:
	/// <see cref="ScalarSelectAliaserTests.Rewrite_leaves_the_assignment_form_alone"/> exercises it
	/// through <c>FindBareSelects</c> (the alias-rewrite path), where a SINGLE column entry is
	/// exactly what matters — verified empirically (2026-08-11) by temporarily changing
	/// <c>ParseColumnList</c>'s terminal-token <c>return null;</c> to <c>return columns;</c>: that
	/// test failed (the rewrite wrongly inserted <c>As [Count]</c> into the assignment), confirming
	/// it is real coverage, not another test that cannot fail.
	/// </para>
	/// </summary>
	[Fact]
	public void Scalar_assignment_is_clean()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.BuildSource("S")], ["--Name: S\nDeclare @Return_A int;\nUse [Db];\nSelect @Return_A = Count(*) From People;\nSelect @Return_A;"], includeSqlServer: true, includeSqlite: false);
		Assert.Empty(diags.Where(d => d.Id == "SP0041"));
	}
}
