using System.Diagnostics;
using System.Runtime.CompilerServices;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Tests.NumberTests;

public class NumberTests
{
	private static string TestHeader(
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

	[Fact]
	public Task DoubleNumberTest()
	{
		Debugger.Launch();

		var name = nameof(DoubleNumberTest);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}			
			Declare @Param_Number1 double;
			Declare @Param_Number4 float;
			Use [Database];
			Select Param_Number1;
			Select Param_Number4;
			"""]);
	}
}
