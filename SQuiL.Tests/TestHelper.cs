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

	/// <summary>
	/// SQLite counterpart to <see cref="TestHeaderPublic"/>: emits an explicit
	/// <c>[SQuiLDialect(SQuiLDialect.Sqlite)]</c>-decorated partial data-context class extending
	/// <c>SqliteDataContext</c> for each of <paramref name="names"/>, for tests exercising the
	/// SQLite header model (Task 5) via <see cref="VerifySqlite"/>.
	/// </summary>
	public static string TestHeaderSqlite(IEnumerable<string> names)
		=> $$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};

			namespace TestCase;

			{{string.Join("", names.Select(p => $$"""
				[{{DialectAttributeName}}(SQuiLDialect.Sqlite)]
				[{{QueryAttributeName}}(QueryFiles.{{p}})]
				public partial class {{p}}DataContext(IConfiguration Configuration) : SqliteDataContext(Configuration)
				{
				}

				"""))}}
			""";

	/// <summary>
	/// PostgreSQL counterpart to <see cref="TestHeaderSqlite"/>: emits an explicit
	/// <c>[SQuiLDialect(SQuiLDialect.Postgres)]</c>-decorated partial data-context class extending
	/// <c>PostgresDataContext</c> for each of <paramref name="names"/>, for tests exercising the
	/// PostgreSQL header model (Task 5) via <see cref="VerifyPostgres"/>.
	/// </summary>
	public static string TestHeaderPostgres(IEnumerable<string> names)
		=> $$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};

			namespace TestCase;

			{{string.Join("", names.Select(p => $$"""
				[{{DialectAttributeName}}(SQuiLDialect.Postgres)]
				[{{QueryAttributeName}}(QueryFiles.{{p}})]
				public partial class {{p}}DataContext(IConfiguration Configuration) : PostgresDataContext(Configuration)
				{
				}

				"""))}}
			""";

	/// <param name="compileCheck">Pass false ONLY for tests whose user sources
	/// are deliberately invalid C#, or that pin a known not-yet-fixed
	/// generator codegen bug (say which in a comment at the call site).</param>
	public static Task Verify(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool compileCheck = true,
		[CallerMemberName] string name = default!,
		[CallerFilePath] string path = default!)
		=> VerifyCore(sources, files, includeProvider: true, includeSqlClient: true, compileCheck, name, path);

	/// <summary>
	/// Identical to <see cref="Verify"/> EXCEPT the compilation does NOT reference
	/// <c>SQuiL.SqlServer</c> (the provider assembly) — so <c>DialectRegistry.IsProviderReferenced</c>
	/// returns false and the generator reports SP0038 instead of emitting a context constructor.
	/// Always pass <c>compileCheck: false</c> — the generated output is intentionally incomplete
	/// (the constructor/base-class file is skipped) and won't compile.
	/// </summary>
	/// <param name="includeSqlClient">
	/// Pass <c>false</c> to ALSO drop <c>Microsoft.Data.SqlClient</c> from the compilation — the
	/// "Core-only" consumer scenario (references SQuiL.Core but neither SqlClient nor the provider
	/// package). Used to prove SP0038 wins over SP0007 when both are absent. Defaults to
	/// <c>true</c> (SqlClient present, only the provider assembly missing).
	/// </param>
	public static Task VerifyWithoutProvider(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool compileCheck = false,
		bool includeSqlClient = true,
		[CallerMemberName] string name = default!,
		[CallerFilePath] string path = default!)
		=> VerifyCore(sources, files, includeProvider: false, includeSqlClient, compileCheck, name, path);

	/// <summary>
	/// SQLite counterpart to <see cref="Verify"/>: the compilation references <c>SQuiL.Sqlite</c>
	/// (+ <c>Microsoft.Data.Sqlite</c> itself) instead of <c>SQuiL.SqlServer</c>, and the Tier-0
	/// compile-check (see <see cref="CompilationAssert.GeneratedCodeCompiles"/>) is told to swap
	/// in the same pair — so a query targeting <c>[SQuiLDialect(SQuiLDialect.Sqlite)]</c> (see
	/// <see cref="TestHeaderSqlite"/>) both generates AND Tier-0-compiles correctly.
	/// </summary>
	public static Task VerifySqlite(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool compileCheck = true,
		[CallerMemberName] string name = default!,
		[CallerFilePath] string path = default!)
		=> VerifyCore(sources, files, includeProvider: false, includeSqlClient: true, compileCheck, name, path, includeSqlite: true);

	/// <summary>
	/// PostgreSQL counterpart to <see cref="Verify"/>: the compilation references
	/// <c>SQuiL.Postgres</c> (+ <c>Npgsql</c> itself) instead of <c>SQuiL.SqlServer</c>, and the
	/// Tier-0 compile-check (see <see cref="CompilationAssert.GeneratedCodeCompiles"/>) is told to
	/// swap in the same pair — so a query targeting <c>[SQuiLDialect(SQuiLDialect.Postgres)]</c>
	/// (see <see cref="TestHeaderPostgres"/>) both generates AND Tier-0-compiles correctly.
	/// </summary>
	public static Task VerifyPostgres(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool compileCheck = true,
		[CallerMemberName] string name = default!,
		[CallerFilePath] string path = default!)
		=> VerifyCore(sources, files, includeProvider: false, includeSqlClient: true, compileCheck, name, path, includePostgres: true);

	static Task VerifyCore(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool includeProvider,
		bool includeSqlClient,
		bool compileCheck,
		string name,
		string path,
		bool includeSqlite = false,
		bool includePostgres = false)
	{
		var syntaxTrees = sources.Select(p => CSharpSyntaxTree.ParseText(p));

		List<MetadataReference> metareferences = [
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location)
		];

		if (includeSqlClient)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location));

		if (includeProvider)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location));

		if (includeSqlite)
		{
			metareferences.Add(MetadataReference.CreateFromFile(typeof(SqliteDataContext).Assembly.Location));
			metareferences.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.Location));
		}

		if (includePostgres)
		{
			metareferences.Add(MetadataReference.CreateFromFile(typeof(PostgresDataContext).Assembly.Location));
			metareferences.Add(MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlConnection).Assembly.Location));
		}

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
			CompilationAssert.GeneratedCodeCompiles(sources, files, includeSqlite: includeSqlite, includePostgres: includePostgres);

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

	/// <summary>
	/// Runs the generator over a single source/query pair with an explicit choice of which
	/// provider assemblies (<c>SQuiL.SqlServer</c> / <c>SQuiL.Sqlite</c> / <c>SQuiL.Postgres</c>)
	/// are referenced, and returns the raw generator-run diagnostics — for tests that assert on
	/// dialect resolution/ambiguity (SP0038/SP0039) rather than on generated-source snapshots.
	/// <c>Microsoft.Data.SqlClient</c> is always referenced (these tests are not exercising the
	/// SP0007 "missing data client" path).
	/// </summary>
	public static ImmutableArray<Diagnostic> RunForDiagnostics(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool includeSqlServer,
		bool includeSqlite,
		bool includePostgres = false)
	{
		var syntaxTrees = sources.Select(p => CSharpSyntaxTree.ParseText(p));

		List<MetadataReference> metareferences = [
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
		];

		if (includeSqlServer)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location));

		if (includeSqlite)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(SqliteDataContext).Assembly.Location));

		if (includePostgres)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(PostgresDataContext).Assembly.Location));

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

		return driver.GetRunResult().Diagnostics;
	}

	/// <summary>
	/// Both <c>SQuiL.SqlServer</c> and <c>SQuiL.Sqlite</c> are referenced with no
	/// <c>[SQuiLDialect]</c> attribute on the data context — dialect resolution is ambiguous
	/// (SP0039).
	/// </summary>
	public static ImmutableArray<Diagnostic> RunWithBothProviders(string source, string query)
		=> RunForDiagnostics([source], [query], includeSqlServer: true, includeSqlite: true);

	/// <summary>
	/// All three of <c>SQuiL.SqlServer</c>, <c>SQuiL.Sqlite</c>, and <c>SQuiL.Postgres</c> are
	/// referenced with no <c>[SQuiLDialect]</c> attribute on the data context — dialect resolution
	/// is ambiguous (SP0039), same rule as <see cref="RunWithBothProviders"/> extended to 3+
	/// referenced providers.
	/// </summary>
	public static ImmutableArray<Diagnostic> RunWithThreeProviders(string source, string query)
		=> RunForDiagnostics([source], [query], includeSqlServer: true, includeSqlite: true, includePostgres: true);

	/// <summary>
	/// Same shape as <see cref="RunForDiagnostics"/>, but additionally references
	/// <c>SQuiL.Core</c> (needed for source strings that write <c>[SQuiLDialect(SQuiLDialect...)]</c>
	/// explicitly) and returns the FULL <see cref="GeneratorDriverRunResult"/> — diagnostics AND
	/// generated source trees — for tests that need to assert on both, e.g. proving a
	/// missing-provider (SP0038) context's table doesn't leak into a sibling context's shared
	/// <c>SQuiLTableMap</c> emission.
	/// </summary>
	public static GeneratorDriverRunResult RunForDiagnosticsAndSources(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		bool includeSqlServer,
		bool includeSqlite,
		bool includePostgres = false)
	{
		var syntaxTrees = sources.Select(p => CSharpSyntaxTree.ParseText(p));

		// Unlike RunForDiagnostics/VerifyCore, this helper needs FULL semantic binding to succeed
		// (resolving [SQuiLDialect(SQuiLDialect...)] attribute usages via SemanticModel.GetSymbolInfo),
		// so — like CompilationAssert.GeneratedCodeCompiles — it must include the BCL reference set;
		// without it, attribute/constant binding silently comes back empty (no diagnostics, just no
		// symbol) rather than throwing.
		List<MetadataReference> metareferences = [
			.. Basic.Reference.Assemblies.Net100.References.All,
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(SQuiLDialectAttribute).Assembly.Location),
		];

		if (includeSqlServer)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location));

		if (includeSqlite)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(SqliteDataContext).Assembly.Location));

		if (includePostgres)
			metareferences.Add(MetadataReference.CreateFromFile(typeof(PostgresDataContext).Assembly.Location));

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

		return driver.GetRunResult();
	}

	/// <summary>
	/// Only <c>SQuiL.Sqlite</c> is referenced with no <c>[SQuiLDialect]</c> attribute — the single
	/// referenced provider is inferred as the dialect (no SP0038, no SP0039).
	/// </summary>
	public static ImmutableArray<Diagnostic> RunWithSqliteProviderOnly(string source, string query)
		=> RunForDiagnostics([source], [query], includeSqlServer: false, includeSqlite: true);
}
