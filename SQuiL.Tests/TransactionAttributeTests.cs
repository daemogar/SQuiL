namespace SQuiL.Tests;

public class TransactionAttributeTests
{
	[Fact]
	public Task TransactionAttributeIsEmittedAndContextCompiles()
	{
		var name = nameof(TransactionAttributeIsEmittedAndContextCompiles);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext { }
			""";
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Param_Id int;
			Use [Database];
			Update [Documents] set Status = 'Done' where Id = @Param_Id;
			"""]);
	}
}
