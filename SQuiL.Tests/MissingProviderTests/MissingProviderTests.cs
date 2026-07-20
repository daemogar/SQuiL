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
}
