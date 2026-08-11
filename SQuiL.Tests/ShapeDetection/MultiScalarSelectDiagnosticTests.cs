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

	/// <summary>A scalar assignment is not a result set at all, so it never trips SP0041.</summary>
	[Fact]
	public void Scalar_assignment_is_clean()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.BuildSource("S")], ["--Name: S\nDeclare @Return_A int;\nUse [Db];\nSelect @Return_A = Count(*) From People;\nSelect @Return_A;"], includeSqlServer: true, includeSqlite: false);
		Assert.Empty(diags.Where(d => d.Id == "SP0041"));
	}
}
