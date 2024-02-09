using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SquilParser.Tests;

public class DataContextGenerationTests
{
	private static readonly string[] queries = [
		@"Queries\Example",
		@"Queries\GetStudentCoursesForEvaluation"
	];

	[Fact] public Task AllQueries() => Verify(queries);
	[Fact] public Task Example() => Verify([queries[0]]);
	[Fact] public Task GetStudentCoursesForEvaluation() => Verify([queries[1]]);
	[Fact] public Task SplitDataContext() => Verify(queries);
	[Fact] public Task SplitDataContextRenamed() => Verify(queries, false);

	private static (string Name, string[] Attributes) Format(string file)
	{
		var name = file.Replace("\\", "");
		return (name, [$"[{AttributeName}(QueryFiles.{name})]"]);
	}

	private static Task Verify(IEnumerable<string> files, bool useGenericName = true)
		=> TestHelper.Verify(
			files.Select(Format).Select(p
				=> GetSource(p.Attributes, useGenericName ? "ApplicationSpecific" : p.Name)),
			files.Select(p => $"{p}.sql"));

	private static string GetSource(IEnumerable<string> attributes, string name) => $$$"""
		using Microsoft.Extensions.Configuration;
		using {{{NamespaceName}}};

		namespace TestCase;

		{{{string.Join(Environment.NewLine, attributes)}}}
		public partial class {{{name}}}DataContext(
			IConfiguration Configuration)
			: {{{BaseDataContextClassName}}}(Configuration)
		{
		}

		public partial record ApplicationSpecificDataContextQueriesExampleRequest() : BaseRecord
		{
			public int Sally1 { get; set; }
		}

		public partial record ApplicationSpecificDataContextQueriesExampleRequestSallyTable() : BaseRecord {}

		public record BaseRecord : AnotherBaseRecord
		{
			public bool Bob { get; init; }		
			public bit Sally2 { get; set; }
		}

		public record AnotherBaseRecord
		{
			public string Bob2 { get; set; }
		}
		""";
}