using Basic.Reference.Assemblies;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL.Generator;

using System.Collections.Immutable;

namespace SQuiL.Tests;

/// <summary>
/// Tier-0 compliance check: runs the generator exactly like TestHelper.Verify,
/// then COMPILES the user sources + generated trees against the real runtime
/// references and fails on any error diagnostic. Verify snapshots alone stay
/// green for generated code that doesn't build.
/// </summary>
public static class CompilationAssert
{
	/// <summary>
	/// The SDK's default ImplicitUsings set. Injected by default so that user sources
	/// in test fixtures can use unqualified names without carrying their own using
	/// directives. Pass <c>injectImplicitUsings: false</c> to test that the generator's
	/// own output is self-sufficient (see <see cref="CompilationTests.GeneratedCodeCompilesWithoutImplicitUsings"/>).
	/// </summary>
	private const string ImplicitUsings = """
		global using System;
		global using System.Collections.Generic;
		global using System.IO;
		global using System.Linq;
		global using System.Net.Http;
		global using System.Threading;
		global using System.Threading.Tasks;
		""";

	public static void GeneratedCodeCompiles(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool injectImplicitUsings = true)
	{
		var syntaxTrees = sources
			.Append(injectImplicitUsings ? ImplicitUsings : string.Empty)
			.Select(p => CSharpSyntaxTree.ParseText(p));

		IEnumerable<MetadataReference> references = [
			.. Net100.References.All,
			MetadataReference.CreateFromFile(typeof(SQuiLBaseDataContext).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(ConfigurationBuilder).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location),
		];

		var additionalFiles = files
			.Select(p => (AdditionalText)(p.StartsWith("--Name:")
				? new AdditionalQuery(p)
				: new AdditionalFile(p)))
			.ToImmutableArray();

		var compilation = CSharpCompilation.Create(
			assemblyName: "Tier0CompileCheck",
			syntaxTrees: syntaxTrees,
			references: references,
			options: new CSharpCompilationOptions(
				OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable));

		var driver = CSharpGeneratorDriver
			.Create(new SQuiLGenerator(true))
			.AddAdditionalTexts(additionalFiles);

		driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

		var errors = output.GetDiagnostics()
			.Where(p => p.Severity == DiagnosticSeverity.Error)
			.ToList();

		if (errors.Count == 0)
			return;

		Assert.Fail($"""
			Generated output does not compile ({errors.Count} error(s)):
			{string.Join(Environment.NewLine, errors.Select(Describe))}
			""");
	}

	private static string Describe(Diagnostic diagnostic)
	{
		var position = diagnostic.Location.GetLineSpan();
		var line = diagnostic.Location.SourceTree?.GetText()
			.Lines[position.StartLinePosition.Line].ToString().Trim();
		return $"""
			  {diagnostic.Id} {position.Path}({position.StartLinePosition.Line + 1}): {diagnostic.GetMessage()}
			      {line}
			""";
	}

}
