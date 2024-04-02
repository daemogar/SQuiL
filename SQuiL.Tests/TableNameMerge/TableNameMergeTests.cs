using System.Diagnostics;
using System.Runtime.CompilerServices;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Tests.TableNameMerge;

public class TableNameMergeTests
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

			[SQuiLTable(TableType.Question)]
			[SQuiLTable(TableType.Questions)]
			public partial record MergedData();
			""";
	}

	[Fact]
	public Task TwoQueriesWithSameReference()
	{
		Debugger.Launch();

		var name = nameof(TwoQueriesWithSameReference);
		return TestHelper.Verify([TestHeader([$"{name}1", $"{name}2"])], [$$"""
			--Name: {{name}}1
			
			Declare @Returns_Questions table(
				Number int,
				[Message] varchar(max)
			);

			Use [Database];

			Select * From @Returns_Questions;
			""", $$"""
			--Name: {{name}}2
			
			Declare @Param_Question table(
				Number int,
				[Message] varchar(max)
			);
			
			Use [Database];
			
			Select * From @Param_Question;
			"""]);
	}
}
