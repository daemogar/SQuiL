using Microsoft.CodeAnalysis;

namespace SQuiL.Tests.EmptyQueryAttribute;

public class EmptyQueryAttributeTests : BaseTest
{
	[Fact]
	public Task EmptyQueryAttribute()
	{
		// compileCheck off: the user source is DELIBERATELY invalid C# (the
		// attribute is missing its required arguments) — the test asserts the
		// generator stays well-behaved on bad input, not that the input builds.
		return TestHelper.Verify(
			[TestHeader(callback: p => $$"""
				[{{SourceGeneratorHelper.QueryAttributeName}}()]
				""")], [], compileCheck: false);
	}
}
