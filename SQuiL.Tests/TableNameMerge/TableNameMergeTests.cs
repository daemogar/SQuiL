using System.Diagnostics;

namespace SQuiL.Tests.TableNameMerge;

public class TableNameMergeTests : BaseTest
{
	[Fact]
	public Task TwoQueriesWithSameReference()
	{
		Debugger.Launch();

		var name = nameof(TwoQueriesWithSameReference);
		return TestHelper.Verify([TestHeader([$"{name}1", $"{name}2"])], [$$"""
			--Name: {{name}}1
			
			Declare @Returns_Questions table(
				Number int,
				[Message] varchar(max)
			);

			Use [Database];

			Select * From @Returns_Questions;
			""", $$"""
			--Name: {{name}}2
			
			Declare @Param_Question table(
				Number int,
				[Message] varchar(max)
			);
			
			Use [Database];
			
			Select * From @Param_Question;
			"""]);
	}
}
