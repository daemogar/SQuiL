using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL.Generator;

using System.Collections.Immutable;

using VerifyXunit;

namespace SquilParser.Tests;

public static class TestHelper
{
	public static Task Verify(IEnumerable<string> sources, IEnumerable<string> files)
	{
		var syntaxTrees = sources.Select(p => CSharpSyntaxTree.ParseText(p));

		IEnumerable<MetadataReference> metareferences = [
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location)
		];

		var additionalFiles = files
			.Select(p => new AdditionalFile(p) as AdditionalText)
			.ToImmutableArray();

		var compilation = CSharpCompilation.Create(
				assemblyName: "Tests",
				references: metareferences,
				syntaxTrees: syntaxTrees);

		var generator = new SQuiLGenerator(true);

		var driver = CSharpGeneratorDriver.Create(generator);

		driver = (CSharpGeneratorDriver)driver.AddAdditionalTexts(additionalFiles);
		driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

		return Verifier.Verify(driver);
	}
}

file class AdditionalFile(string Path) : AdditionalText
{
	public override string Path { get; } = Path;

	public override SourceText? GetText(CancellationToken cancellationToken = default)
	{
		var text = File.ReadAllText($"..\\..\\..\\{Path}");
		return SourceText.From(text);
	}
}