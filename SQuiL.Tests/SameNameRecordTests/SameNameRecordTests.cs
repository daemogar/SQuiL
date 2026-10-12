namespace SQuiL.Tests.SameNameRecord;

/// <summary>
/// TODO #27: a consumer record that merely shares a generated type's simple name must not be
/// mistaken for the user's partial of that type.
/// </summary>
public class SameNameRecordTests : BaseTest
{
	[Fact]
	public Task UnrelatedRecordInOtherNamespaceDoesNotSuppressRowRecord()
	{
		var name = nameof(UnrelatedRecordInOtherNamespaceDoesNotSuppressRowRecord);
		return TestHelper.Verify(
			[TestHeader([name]), """
			namespace Other.Colleague;

			public sealed record Institution(string ID, string Title);
			"""],
			[$$"""
			--Name: {{name}}
			Declare @Param_Institution table(InstitutionID int, SchoolName varchar(50));
			Declare @Returns_Institution table(InstitutionID int, SchoolName varchar(50));
			Use [Database];
			Select InstitutionID, SchoolName From @Param_Institution;
			"""]);
	}

	[Fact]
	public Task UnrelatedPartialRecordInOtherNamespaceDoesNotSuppressRowRecord()
	{
		var name = nameof(UnrelatedPartialRecordInOtherNamespaceDoesNotSuppressRowRecord);
		return TestHelper.Verify(
			[TestHeader([name]), """
			namespace Other.Colleague;

			public partial record Institution(string ID, string Title);
			"""],
			[$$"""
			--Name: {{name}}
			Declare @Returns_Institution table(InstitutionID int, SchoolName varchar(50));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task UnrelatedRecordNamedLikeRequestDoesNotAffectRequest()
	{
		var name = nameof(UnrelatedRecordNamedLikeRequestDoesNotAffectRequest);
		return TestHelper.Verify(
			[TestHeader([name]), $$"""
			namespace Other.Colleague;

			internal sealed record {{name}}Request(string ID);
			"""],
			[$$"""
			--Name: {{name}}
			Declare @Param_Count int;
			Declare @Return_Total int;
			Use [Database];
			Set @Return_Total = @Param_Count;
			Select @Return_Total;
			"""]);
	}

	[Fact]
	public Task UserPartialRowRecordInModelsNamespaceStillMerges()
	{
		var name = nameof(UserPartialRowRecordInModelsNamespaceStillMerges);
		return TestHelper.Verify(
			[TestHeader([name]), """
			namespace TestCase.Models;

			public partial record Institution()
			{
				public string Note { get; set; } = "";
			}
			"""],
			[$$"""
			--Name: {{name}}
			Declare @Returns_Institution table(InstitutionID int, SchoolName varchar(50));
			Use [Database];
			Select 1;
			"""]);
	}
}
