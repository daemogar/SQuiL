using Basic.Reference.Assemblies;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL.Generator;

using System.Collections.Immutable;

namespace SQuiL.Tests;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

/// <summary>
/// TODO #28: the generator also runs in projects that only reference a SQuiL library. It must
/// stay silent there, and its types must not leak across the assembly boundary.
/// </summary>
public class ConsumerProjectTests
{
	private static readonly string LibrarySource = $$"""
		using Microsoft.Extensions.Configuration;
		using {{NamespaceName}};

		[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]

		namespace Library;

		[{{QueryAttributeName}}(QueryFiles.GetCount)]
		public partial class LibraryDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration)
		{
		}
		""";

	private const string LibraryQuery = """
		--Name: GetCount
		Declare @Return_Count int;
		Use [Db];
		Set @Return_Count = 1;
		Select @Return_Count;
		""";

	private const string ConsumerSource = """
		using Microsoft.Extensions.DependencyInjection;

		namespace Consumer;

		public static class Startup
		{
			public static IServiceCollection Configure(IServiceCollection services) => services.AddSQuiL();

			public static int QueryCount => typeof(SQuiL.QueryFiles).GetFields().Length;
		}
		""";

	[Fact]
	public void Project_without_queries_or_contexts_gets_no_AddSQuiL_and_no_diagnostics()
	{
		var (_, result) = Compile("Consumer", ConsumerSource, files: [], references: []);

		Assert.DoesNotContain(result.GeneratedTrees, p => p.FilePath.EndsWith($"{NamespaceName}Extensions.g.cs"));
		Assert.Empty(result.Diagnostics);
	}

	[Fact]
	public void Consumer_of_a_SQuiL_library_builds_cleanly_and_binds_the_library_AddSQuiL()
	{
		var (library, _) = Compile("Library", LibrarySource, [LibraryQuery], []);
		AssertNoErrors(library);

		using MemoryStream image = new();
		var emit = library.Emit(image);
		Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

		var (consumer, _) = Compile("Consumer", ConsumerSource, [],
			[MetadataReference.CreateFromImage(image.ToArray())]);

		var problems = consumer.GetDiagnostics()
			.Where(p => p.Severity == DiagnosticSeverity.Error || p.Id == "CS0436")
			.ToList();
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

		var tree = consumer.SyntaxTrees.First(p => p.ToString().Contains("namespace Consumer;"));
		var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
			.First(p => p.ToString().Contains("AddSQuiL"));
		var method = consumer.GetSemanticModel(tree).GetSymbolInfo(call).Symbol;
		Assert.Equal("Library", method?.ContainingAssembly.Name);
	}

	private static (Compilation Output, GeneratorDriverRunResult Result) Compile(
		string assemblyName,
		string source,
		IEnumerable<string> files,
		IEnumerable<MetadataReference> references)
	{
		var compilation = CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			[
				.. Net100.References.All,
				MetadataReference.CreateFromFile(typeof(SQuiLBaseDataContext).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location),
				.. references,
			],
			new CSharpCompilationOptions(
				OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable));

		var driver = CSharpGeneratorDriver
			.Create(new SQuiLGenerator(true))
			.AddAdditionalTexts(files.Select(p => (AdditionalText)new AdditionalQuery(p)).ToImmutableArray())
			.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

		return (output, driver.GetRunResult());
	}

	private static void AssertNoErrors(Compilation compilation)
	{
		var errors = compilation.GetDiagnostics().Where(p => p.Severity == DiagnosticSeverity.Error).ToList();
		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
	}
}
