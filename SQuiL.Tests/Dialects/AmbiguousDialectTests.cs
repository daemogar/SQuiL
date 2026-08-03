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

	[Fact]
	public void Three_providers_no_attribute_reports_SP0039()
	{
		// SqlServer + Sqlite + Postgres all referenced, no [SQuiLDialect] attribute -> still
		// ambiguous (SP0039) — the 2+ rule holds for 3+ referenced providers too.
		var diagnostics = TestHelper.RunWithThreeProviders(
			TestHelper.BuildSource("Sample"),
			"--Name: Sample\nDeclare @Param_X int;\nUse [Db];\nSelect 1;");

		var sp = System.Linq.Enumerable.ToList(
			System.Linq.Enumerable.Where(diagnostics, d => d.Id == "SP0039"));
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
		Assert.Contains("SQuiLDialect", sp[0].GetMessage());
	}

	[Fact]
	public void Explicit_postgres_attribute_resolves_without_ambiguity()
	{
		// All three providers are referenced (so an unattributed context here WOULD be
		// ambiguous — see Three_providers_no_attribute_reports_SP0039), but this context
		// explicitly targets Postgres via [SQuiLDialect(SQuiLDialect.Postgres)] -> resolves
		// cleanly to dialect id 2 (no SP0039), and SQuiL.Postgres IS referenced (no SP0038
		// either). The query body is variable-free (`Select 1;`) so no PostgresDialect member
		// that still throws NotImplementedException (Tasks 4-6) is ever invoked.
		const string source = """
			using Microsoft.Extensions.Configuration;
			using SQuiL;

			namespace TestCase;

			[SQuiLQueryAttribute(QueryFiles.Sample)]
			[SQuiLDialect(SQuiLDialect.Postgres)]
			public partial class SampleDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration)
			{
			}
			""";

		var result = TestHelper.RunForDiagnosticsAndSources(
			[source],
			["--Name: Sample\nSelect 1;"],
			includeSqlServer: true,
			includeSqlite: true,
			includePostgres: true);

		Assert.DoesNotContain(result.Diagnostics, d => d.Id is "SP0038" or "SP0039");
	}
}
