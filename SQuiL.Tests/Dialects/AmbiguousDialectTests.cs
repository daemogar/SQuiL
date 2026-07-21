namespace SQuiL.Tests.Dialects;

using Microsoft.CodeAnalysis;

using Xunit;

/// <summary>
/// SP0039 — 2+ SQuiL provider packages are referenced by the compilation and a data context
/// declares no <c>[SQuiLDialect]</c> attribute, so the generator cannot infer which dialect it
/// targets. Complements <c>MissingProviderTests</c> (SP0038, exactly one provider expected but
/// not referenced) and <see cref="DialectRegistryTests"/> (explicit dialect resolution) — this
/// class covers the "single referenced provider infers the dialect" / "2+ referenced providers
/// with no explicit choice is ambiguous" resolution rules added in Task 3.
/// </summary>
public class AmbiguousDialectTests
{
	[Fact]
	public void Two_providers_no_attribute_reports_SP0039()
	{
		var diagnostics = TestHelper.RunWithBothProviders(
			TestHelper.BuildSource("Sample"),
			"--Name: Sample\nDeclare @Param_X int;\nUse [Db];\nSelect 1;");

		var sp = System.Linq.Enumerable.ToList(
			System.Linq.Enumerable.Where(diagnostics, d => d.Id == "SP0039"));
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
		Assert.Contains("SQuiLDialect", sp[0].GetMessage());
	}

	[Fact]
	public void Single_provider_infers_dialect_without_attribute()
	{
		// Only the Sqlite provider referenced -> resolves to Sqlite, no SP0038, no SP0039.
		var diagnostics = TestHelper.RunWithSqliteProviderOnly(
			TestHelper.BuildSource("Sample"),
			"--Name: Sample\nCreate Temp Table Param_X (Value INTEGER);\nSelect 1;");
		Assert.DoesNotContain(diagnostics, d => d.Id is "SP0038" or "SP0039");
	}
}
