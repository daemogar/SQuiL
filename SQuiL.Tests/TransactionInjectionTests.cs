namespace SQuiL.Tests;

public class TransactionInjectionTests
{
	[Fact]
	public Task EnabledTransactionWrapsReaderPath()
	{
		var name = nameof(EnabledTransactionWrapsReaderPath);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext { }
			""";
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Param_Id int;
			Declare @Return_Count int;
			Use [Database];
			Update [Documents] set Status = 'Done' where Id = @Param_Id;
			Set @Return_Count = @@RowCount;
			Select @Return_Count;
			"""]);
	}

	[Fact]
	public Task EnabledTransactionWrapsNonQueryPath()
	{
		var name = nameof(EnabledTransactionWrapsNonQueryPath);
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
