namespace SQuiL.Tests;

/// <summary>
/// Verifies the three transaction-diagnostic rules:
///   SP0023 — [SQuiLQuery] (or disabled transaction) wraps a body with a persistent real-table mutation.
///   SP0024 — [SQuiLQueryTransaction] (enabled) wraps a provably read-only body.
///   SP0025 — [SQuiLQueryTransaction] (enabled) body contains its own Begin Tran.
/// All tests use compileCheck:false — SP0023/SP0024 are warnings (build succeeds) but the
/// snapshot comparison requires the received file to match exactly, and SP0025 is an error.
/// </summary>
public class TransactionDiagnosticTests
{
	/// <summary>
	/// SP0023 — [SQuiLQuery] with a body that contains an Update on a real table should warn.
	/// </summary>
	[Fact]
	public Task MutationUnderPlainQueryWarnsSP0023()
	{
		var name = nameof(MutationUnderPlainQueryWarnsSP0023);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQuery(QueryFiles.{{name}})]
			public partial class {{name}}DataContext { }
			""";
		// compileCheck:false — SP0023 is a warning; snapshot must match exactly.
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Param_Id int;
			Use [Database];
			Update [Documents] Set Status = 'Done' Where Id = @Param_Id;
			"""], compileCheck: false);
	}

	/// <summary>
	/// SP0024 — [SQuiLQueryTransaction] (enabled:true by default) wrapping a read-only Select should warn.
	/// </summary>
	[Fact]
	public Task ReadOnlyUnderTransactionWarnsSP0024()
	{
		var name = nameof(ReadOnlyUnderTransactionWarnsSP0024);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext { }
			""";
		// compileCheck:false — SP0024 is a warning; snapshot must match exactly.
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Param_Id int;
			Declare @Return_Count int;
			Use [Database];
			Set @Return_Count = (Select Count(*) From [Documents] Where Id = @Param_Id);
			Select @Return_Count;
			"""], compileCheck: false);
	}

	/// <summary>
	/// SP0025 — [SQuiLQueryTransaction] (enabled) body contains its own Begin Tran — error.
	/// </summary>
	[Fact]
	public Task OwnBeginTranUnderEnabledTransactionErrorsSP0025()
	{
		var name = nameof(OwnBeginTranUnderEnabledTransactionErrorsSP0025);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class {{name}}DataContext { }
			""";
		// compileCheck:false — SP0025 is an error diagnostic.
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Declare @Param_Id int;
			Use [Database];
			Begin Tran;
			Update [Documents] Set Status = 'Done' Where Id = @Param_Id;
			Commit;
			"""], compileCheck: false);
	}
}
