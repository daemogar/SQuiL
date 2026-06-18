using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL.Generator;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace SQuiL.Tests;

public static class TestHelper
{
	/// <param name="compileCheck">Pass false ONLY for tests whose user sources
	/// are deliberately invalid C#, or that pin a known not-yet-fixed
	/// generator codegen bug (say which in a comment at the call site).</param>
	public static Task Verify(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool compileCheck = true,
		[CallerMemberName] string name = default!,
		[CallerFilePath] string path = default!)
	{
		var syntaxTrees = sources.Select(p => CSharpSyntaxTree.ParseText(p));

		IEnumerable<MetadataReference> metareferences = [
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location)
		];

		var additionalFiles = files
			.Select(p => (AdditionalText)(p.StartsWith("--Name:")
				? new AdditionalQuery(p)
				: new AdditionalFile(p)))
			.ToImmutableArray();

		var compilation = CSharpCompilation.Create(
				assemblyName: "Tests",
				references: metareferences,
				syntaxTrees: syntaxTrees);

		var generator = new SQuiLGenerator(true);

		var driver = CSharpGeneratorDriver.Create(generator);

		driver = (CSharpGeneratorDriver)driver.AddAdditionalTexts(additionalFiles);
		driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

		// Tier-0: whenever the generator claims success (no error diagnostics
		// of its own), its output must actually compile. Error-path tests are
		// exempt — their (possibly partial) output is asserted via snapshots.
		if (compileCheck
			&& !driver.GetRunResult().Diagnostics.Any(p => p.Severity == DiagnosticSeverity.Error))
			CompilationAssert.GeneratedCodeCompiles(sources, files);

		VerifySettings settings = default!;
		if (path is not null)
		{
			path = Path.Combine(Path.GetDirectoryName(path)!, name) + Path.DirectorySeparatorChar;
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);
			settings = new();
			settings.UseDirectory(path);
			//settings.UseTypeName("bob");

			// Token.Offset is an absolute character position into the input SQL,
			// so it shifts with CRLF vs LF line endings (autocrlf checkouts vs
			// LF/CI). It carries no behavioral meaning in these AST-dump
			// snapshots, so scrub it to keep snapshots line-ending-independent
			// across Windows and Linux CI.
			settings.AddScrubber(builder =>
			{
				var scrubbed = System.Text.RegularExpressions.Regex.Replace(
					builder.ToString(), @"Offset = \d+", "Offset = {scrubbed}");
				builder.Clear();
				builder.Append(scrubbed);
			});
		}

		return Verifier.Verify(driver, settings);
	}
}
