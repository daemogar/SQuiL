using System.Runtime.CompilerServices;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Tests;

public class ErrorQueryTests
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
	public Task QueryHasErrorObject()
	{
		var name = nameof(QueryHasErrorObject);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}			
			Declare @Debug bit = 1;
			Declare @Param_Elapsed int;

			Declare @Return_SampleID int;
			Declare @Return_SampleEntity table(ID int);
			Declare @Returns_Samples table(ID int);
			
			Declare @Error table(
				Number int,
				Severity int,
				[State] int,
				Line int,
				[Procedure] varchar(max),
				[Message] varchar(max)
			);

			Use DataRepository;
			"""]);
	}

	[Fact]
	public Task QueryHasErrorList()
	{
		var name = nameof(QueryHasErrorList);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}			
			Declare @Debug bit = 1;
			Declare @Param_Elapsed int;

			Declare @Return_SampleID int;
			Declare @Return_SampleEntity table(ID int);
			Declare @Returns_Samples table(ID int);

			Declare @Errors table(
				Number int,
				Severity int,
				[State] int,
				Line int,
				[Procedure] varchar(max),
				[Message] varchar(max)
			);

			Use DataRepository;
			"""]);
	}
}