namespace SQuiL.Tests;

public class OneToOneMappingTests
{
	[Fact]
	public Task SameQueryFileOnTwoContextsIsError()
	{
		var name = nameof(SameQueryFileOnTwoContextsIsError);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQuery(QueryFiles.{{name}})]
			public partial class FirstDataContext { }
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class SecondDataContext { }
			""";
		// compileCheck:false — we expect a diagnostic, not clean generation.
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Use [Database];
			Select 1;
			"""], compileCheck: false);
	}

	[Fact]
	public Task BothAttributesOnOneClassIsError()
	{
		var name = nameof(BothAttributesOnOneClassIsError);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQuery(QueryFiles.{{name}})]
			[SQuiLQueryTransaction(QueryFiles.{{name}})]
			public partial class BothAttributesDataContext { }
			""";
		// compileCheck:false — we expect a diagnostic, not clean generation.
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Use [Database];
			Select 1;
			"""], compileCheck: false);
	}

	[Fact]
	public Task SameQueryFileOnTwoQueryContextsIsError()
	{
		var name = nameof(SameQueryFileOnTwoQueryContextsIsError);
		var source = $$"""
			using SQuiL;
			namespace TestCase;
			[SQuiLQuery(QueryFiles.{{name}})]
			public partial class FirstDataContext { }
			[SQuiLQuery(QueryFiles.{{name}})]
			public partial class SecondDataContext { }
			""";
		// compileCheck:false — we expect a diagnostic, not clean generation.
		// This variant confirms SP0027 fires when both registrations use [SQuiLQuery]
		// (the existing SameQueryFileOnTwoContextsIsError test covers [SQuiLQuery]+[SQuiLQueryTransaction]).
		return TestHelper.Verify([source], [$$"""
			--Name: {{name}}
			Use [Database];
			Select 1;
			"""], compileCheck: false);
	}
}
