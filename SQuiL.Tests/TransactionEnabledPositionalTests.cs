namespace SQuiL.Tests;

/// <summary>
/// Verifies that positional (non-named) arguments to [SQuiLQueryTransaction]
/// are honoured by the generator — specifically that slot 2 = enabled and
/// slot 3 = debugRollback work just like their named equivalents.
/// </summary>
public class TransactionEnabledPositionalTests
{
	/// <summary>
	/// [SQuiLQueryTransaction(QueryFiles.X, "Db", false)] must NOT inject a
	/// transaction wrapper.  The positional third arg is enabled=false.
	/// </summary>
	[Fact]
	public Task PositionalEnabledFalse_NoTransactionInjected()
	{
		var name = nameof(PositionalEnabledFalse_NoTransactionInjected);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}}, "Db", false)]
			public partial class {{name}}DataContext { }
			""";
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Param_Id int;
			Declare @Return_Count int;
			Use [Db];
			Update [Documents] set Status = 'Done' where Id = @Param_Id;
			Set @Return_Count = @@RowCount;
			Select @Return_Count;
			"""]);
	}

	/// <summary>
	/// [SQuiLQueryTransaction(QueryFiles.X, "Db", true, false)] must inject
	/// a transaction wrapper but ALWAYS commit (debugRollback=false positional).
	/// </summary>
	[Fact]
	public Task PositionalDebugRollbackFalse_AlwaysCommits()
	{
		var name = nameof(PositionalDebugRollbackFalse_AlwaysCommits);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}}, "Db", true, false)]
			public partial class {{name}}DataContext { }
			""";
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Debug bit = 0;
			Declare @Return_Count int;
			Use [Db];
			Update [Documents] set Status = 'Done';
			Set @Return_Count = @@RowCount;
			Select @Return_Count;
			"""]);
	}
}
