namespace SQuiL.Tests;

public class TransactionDebugRollbackTests
{
	/// <summary>
	/// [SQuiLQueryTransaction] + @Debug + @SuppressDebug → the hoisted debug var
	/// uses the SuppressDebug guard form: !request.SuppressDebug && (request.Debug || ...).
	/// The commit is gated on !__debug (rollback in debug mode).
	/// </summary>
	[Fact]
	public Task DebugRollbackWithSuppressDebug()
	{
		var name = nameof(DebugRollbackWithSuppressDebug);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext { }
			""";
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Debug bit = 0;
			Declare @SuppressDebug bit = 0;
			Declare @Return_Count int;
			Use [Database];
			Update [Documents] set Status = 'Done';
			Set @Return_Count = @@RowCount;
			Select @Return_Count;
			"""]);
	}

	[Fact]
	public Task DebugRollbackHoistsDebugAndGatesCommit()
	{
		var name = nameof(DebugRollbackHoistsDebugAndGatesCommit);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext { }
			""";
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Debug bit = 0;
			Declare @Return_Count int;
			Use [Database];
			Update [Documents] set Status = 'Done';
			Set @Return_Count = @@RowCount;
			Select @Return_Count;
			"""]);
	}

	[Fact]
	public Task DebugRollbackFalseAlwaysCommits()
	{
		var name = nameof(DebugRollbackFalseAlwaysCommits);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}}, debugRollback: false)]
			public partial class {{name}}DataContext { }
			""";
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Debug bit = 0;
			Declare @Return_Count int;
			Use [Database];
			Update [Documents] set Status = 'Done';
			Set @Return_Count = @@RowCount;
			Select @Return_Count;
			"""]);
	}

	[Fact]
	public Task TransactionWithoutDebugAlwaysCommits()
	{
		var name = nameof(TransactionWithoutDebugAlwaysCommits);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext { }
			""";
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Return_Count int;
			Use [Database];
			Update [Documents] set Status = 'Done';
			Set @Return_Count = @@RowCount;
			Select @Return_Count;
			"""]);
	}
}
