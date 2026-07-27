using Microsoft.CodeAnalysis;

using System.Linq;

using Xunit;

namespace SQuiL.Tests.Ordering;

/// <summary>
/// SP0040 — params-before-returns ordering. Within one SQuiL file, every INPUT
/// (@Param/@Params) declaration must precede any OUTPUT (@Return/@Returns) declaration.
/// The severity is dialect-dependent: an ERROR when the resolved dialect is SQLite
/// (whose Create-Temp-Table header must declare inputs first for the generated shred
/// to see them), a WARNING otherwise (SQL Server tolerates the interleave). Mirrors the
/// explicit-inspection style of <see cref="ScalarMarkerDiagnosticTests"/> — only the
/// diagnostic Id and Severity matter here, not full snapshots.
/// </summary>
public class OrderingDiagnosticTests
{
	[Fact]
	public void SqlServer_return_before_param_is_SP0040_warning()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.BuildSource("S")], ["--Name: S\nDeclare @Return_A int;\nDeclare @Param_B int;\nUse [Db];\nSelect 1;"], includeSqlServer: true, includeSqlite: false);
		var sp = diags.Where(d => d.Id == "SP0040").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Warning, sp[0].Severity);
	}

	[Fact]
	public void Sqlite_return_before_param_is_SP0040_error()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.TestHeaderSqlite(["S"])], ["--Name: S\nCreate Temp Table Return_A (Value INTEGER);\nCreate Temp Table Param_B (Value INTEGER);\nSelect 1;"], includeSqlServer: false, includeSqlite: true);
		var sp = diags.Where(d => d.Id == "SP0040").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
	}
}
