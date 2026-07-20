namespace SQuiL.Tests.MissingProviderTests;

using Xunit;

/// <summary>
/// SP0038 — a data context resolves to a dialect whose provider runtime base type is not
/// referenced by the compilation (e.g. the consumer referenced SQuiL.Core but not
/// SQuiL.SqlServer). Verified against a compilation built WITHOUT the provider assembly
/// (see <see cref="TestHelper.VerifyWithoutProvider"/>) so the probe actually fails, unlike
/// every other test in the suite whose compilation includes the provider (Task 2, Step 5).
/// </summary>
public class MissingProviderTests
{
	[Fact]
	public Task MissingProvider_reports_SP0038()
		=> TestHelper.VerifyWithoutProvider(
			[TestHelper.BuildSource("Sample")],
			["""
			--Name: Sample
			Declare @Return_Count int;
			Use [Db];
			Set @Return_Count = (Select Count(*) From X);
			Select @Return_Count;
			"""]);

	/// <summary>
	/// I1 (final-review fix): a consumer who references SQuiL.Core but NEITHER
	/// Microsoft.Data.SqlClient NOR SQuiL.SqlServer must see SP0038 ("add SQuiL.SqlServer"), not
	/// the less-actionable SP0007 ("add Microsoft.Data.SqlClient"). Before the fix, the
	/// missingDataClient/SP0007 guard ran first and `continue`d before the SP0038 check was ever
	/// reached, so this scenario silently reported the wrong diagnostic.
	/// </summary>
	[Fact]
	public Task MissingProvider_and_MissingSqlClient_reports_SP0038_not_SP0007()
		=> TestHelper.VerifyWithoutProvider(
			[TestHelper.BuildSource("Sample")],
			["""
			--Name: Sample
			Declare @Return_Count int;
			Use [Db];
			Set @Return_Count = (Select Count(*) From X);
			Select @Return_Count;
			"""],
			includeSqlClient: false);
}
