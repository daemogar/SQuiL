using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL;
using SQuiL.Generator;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace SQuiL.Tests;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

public static class TestHelper
{
	/// <summary>
	/// Public test-header builder mirroring the per-class private <c>TestHeader</c> helpers:
	/// emits a <c>[SQuiLQuery(QueryFiles.&lt;name&gt;)]</c>-decorated partial data-context class
	/// for the given query name(s). Shared here so fixtures in any test class (e.g. ShapeDetection)
	/// can build the standard header without duplicating it.
	/// </summary>
	public static string TestHeaderPublic(
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
			public partial class {{name}}DataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration)
			{
			}
			""";
	}

	/// <summary>
	/// Thin single-name wrapper around <see cref="TestHeaderPublic"/>: builds the standard
	/// <c>[SQuiLQuery(QueryFiles.&lt;queryName&gt;)]</c> partial data-context class scaffold
	/// (<c>&lt;queryName&gt;DataContext(IConfiguration) : SqlServerDataContext(...)</c>) keyed
	/// off a single query name, for tests that don't need multiple attributes/callers.
	/// </summary>
	public static string BuildSource(string queryName)
		=> TestHeaderPublic([queryName], name: queryName);

	/// <param name="compileCheck">Pass false ONLY for tests whose user sources
	/// are deliberately invalid C#, or that pin a known not-yet-fixed
	/// generator codegen bug (say which in a comment at the call site).</param>
	public static Task Verify(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool compileCheck = true,
		[CallerMemberName] string name = default!,
		[CallerFilePath] string path = default!)
		=> VerifyCore(sources, files, includeProvider: true, compileCheck, name, path);

	/// <summary>
	/// Identical to <see cref="Verify"/> EXCEPT the compilation does NOT reference
	/// <c>SQuiL.SqlServer</c> (the provider assembly) — so <c>DialectRegistry.IsProviderReferenced</c>
	/// returns false and the generator reports SP0038 instead of emitting a context constructor.
	/// Always pass <c>compileCheck: false</c> — the generated output is intentionally incomplete
	/// (the constructor/base-class file is skipped) and won't compile.
	/// </summary>
	public static Task VerifyWithoutProvider(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool compileCheck = false,
		[CallerMemberName] string name = default!,
		[CallerFilePath] string path = default!)
		=> VerifyCore(sources, files, includeProvider: false, compileCheck, name, path);

	static Task VerifyCore(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool includeProvider,
		bool compileCheck,
		string name,
		string path)
	{
		var syntaxTrees = sources.Select(p => CSharpSyntaxTree.ParseText(p));

		List<MetadataReference> metareferences = [
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location)
		];

		if (includeProvider)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location));

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
