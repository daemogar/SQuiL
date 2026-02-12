using Microsoft.CodeAnalysis;

namespace SQuiL.Tests.EmptyQueryAttribute;

public class EmptyQueryAttributeTests : BaseTest
{
	[Fact]
	public Task EmptyQueryAttribute()
	{
		return TestHelper.Verify(
			[TestHeader(callback: p => $$"""
				[{{SourceGeneratorHelper.QueryAttributeName}}()]
				""")], []);
	}
}
