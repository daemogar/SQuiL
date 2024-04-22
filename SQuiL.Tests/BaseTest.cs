using Microsoft.CodeAnalysis;

using System.Runtime.CompilerServices;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Tests;

public abstract class BaseTest
{
	protected static Task TestQueryParamsAndReturns(string query, [CallerMemberName] string name = default!)
	{
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			{{query}}
			Use [Database];
			"""]);
	}

	protected static string TestHeader(
		IEnumerable<string> attributes = default!,
		Func<string, string> callback = default!,
		[CallerMemberName] string name = default!)
	{
		attributes ??= [name];
		callback ??= p => $$"""
			[{{QueryAttributeName}}(QueryFiles.{{p}})]
			""";

		return $$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};
		
			namespace TestCase;
		
			{{string.Join("", attributes.Select(callback))}}
			public partial class {{name}}DataContext(IConfiguration Configuration) : {{BaseDataContextClassName}}(Configuration)
			{
			}
			""";
	}
}
