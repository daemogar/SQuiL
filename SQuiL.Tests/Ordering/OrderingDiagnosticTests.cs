using Microsoft.CodeAnalysis;

using System.Linq;

using Xunit;

namespace SQuiL.Tests.Ordering;

/// <summary>
/// SP0040 — params-before-returns ordering. Within one SQuiL file, every INPUT
/// (@Param/@Params) declaration must precede any OUTPUT (@Return/@Returns) declaration.
/// The severity is dialect-dependent: an ERROR for every temp-table-header dialect (SQLite,
/// PostgreSQL — both must declare inputs first in their Create-Temp-Table header for the
/// generated shred to see them), a WARNING otherwise (SQL Server tolerates the interleave).
/// Mirrors the explicit-inspection style of <see cref="ScalarMarkerDiagnosticTests"/> — only the
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

	/// <summary>
	/// Task 8 (Phase 3 Postgres): PostgreSQL is also a temp-table-header dialect (its header must
	/// create every input before any output is declared), so it gets the same ERROR severity as
	/// SQLite — not the SQL Server WARNING.
	/// </summary>
	[Fact]
	public void Postgres_return_before_param_is_SP0040_error()
	{
		var diags = TestHelper.RunForDiagnostics([TestHelper.TestHeaderPostgres(["S"])], ["--Name: S\nCreate Temp Table Return_A (Value int);\nCreate Temp Table Param_B (Value int);\nSelect 1;"], includeSqlServer: false, includeSqlite: false, includePostgres: true);
		var sp = diags.Where(d => d.Id == "SP0040").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
	}
}
