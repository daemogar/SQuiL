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
}
