using Basic.Reference.Assemblies;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL.Generator;

using System.Collections.Immutable;

namespace SQuiL.Tests;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

/// <summary>
/// A required, non-nullable reference-type request parameter (<c>Declare @Param_X varchar(10);</c>,
/// no <c>= null</c>) used to be emitted as <c>public string X { get; set; }</c> under
/// <c>#nullable enable</c>, which raised CS8618 once per property in every consumer. It is now
/// emitted as <c>public required string X { get; set; }</c> when the consumer's compilation
/// supports required members (C# 11+ and the runtime attributes), and unchanged otherwise.
/// These tests compile against the real .NET 10 reference set, unlike the snapshot harness,
/// whose bare compilation has no BCL and so never sees <c>RequiredMemberAttribute</c>.
/// </summary>
public class RequiredRequestMemberTests
{
	private static readonly string Context = $$"""
		using {{NamespaceName}};

		namespace TestCase;

		[{{QueryAttributeName}}(QueryFiles.Save)]
		public partial class SaveDataContext { }
		""";

	private const string Query = """
		--Name: Save
		Declare @Param_WorkID varchar(36);
		Declare @Param_Note varchar(50) = null;
		Declare @Param_Label varchar(10) = 'x';
		Declare @Param_Payload varbinary(max);
		Declare @Param_Count int;
		Declare @Return_Total int;
		Use MyDatabase;
		Select @Return_Total = Count(*) From Work Where WorkID = @Param_WorkID;
		Select @Return_Total;
		""";

	private const string GlobalUsings = """
		global using System;
		global using System.Collections.Generic;
		global using System.Linq;
		global using System.Threading;
		global using System.Threading.Tasks;
		""";

	[Fact]
	public void RequiredReferenceTypeParamsAreEmittedAsRequired()
	{
		var request = GeneratedRequest(Run(LanguageVersion.Latest).Generated);

		Assert.Contains("public required string WorkID { get; set; }", request);
		Assert.Contains("public required byte[] Payload { get; set; }", request);

		// Unchanged: nullable, defaulted and value-type params never raise CS8618.
		Assert.Contains("public string? Note { get; set; }", request);
		Assert.Contains("public string Label { get; set; } = \"x\";", request);
		Assert.Contains("public int Count { get; set; }", request);
		Assert.DoesNotContain("required string? Note", request);
		Assert.DoesNotContain("required string Label", request);
		Assert.DoesNotContain("required int", request);
	}

	[Fact]
	public void GeneratedRequestRaisesNoCS8618()
	{
		var diagnostics = Run(LanguageVersion.Latest).Output.GetDiagnostics();

		Assert.DoesNotContain(diagnostics, p => p.Id == "CS8618");
		Assert.DoesNotContain(diagnostics, p => p.Severity == DiagnosticSeverity.Error);
	}

	[Fact]
	public void CallerWithObjectInitializerCompiles()
	{
		var diagnostics = Run(LanguageVersion.Latest, """
			namespace TestCase;

			public static class Caller
			{
				public static SaveRequest Build() => new()
				{
					WorkID = "abc",
					Payload = [1, 2, 3],
				};
			}
			""").Output.GetDiagnostics();

		Assert.DoesNotContain(diagnostics, p => p.Severity == DiagnosticSeverity.Error);
		Assert.DoesNotContain(diagnostics, p => p.Id == "CS8618");
	}

	[Fact]
	public void CallerThatOmitsARequiredParamIsACompileError()
	{
		var diagnostics = Run(LanguageVersion.Latest, """
			namespace TestCase;

			public static class Caller
			{
				public static SaveRequest Build() => new() { Payload = [1] };
			}
			""").Output.GetDiagnostics();

		Assert.Contains(diagnostics, p => p.Id == "CS9035" && p.GetMessage().Contains("WorkID"));
	}

	// Only the request shape is asserted: the rest of the generated code already needs C# 12
	// (collection expressions), so a C# 10 compilation has errors that predate this change.
	[Fact]
	public void PreCSharp11ConsumerKeepsThePreviousShape()
	{
		var request = GeneratedRequest(Run(LanguageVersion.CSharp10).Generated);

		Assert.Contains("public string WorkID { get; set; }", request);
		Assert.DoesNotContain("required", request);
	}

	private static string GeneratedRequest(ImmutableArray<SyntaxTree> generated)
		=> generated.Single(p => p.FilePath.EndsWith("SaveRequest.g.cs")).ToString();

	private static (ImmutableArray<SyntaxTree> Generated, Compilation Output) Run(
		LanguageVersion languageVersion, string? caller = null)
	{
		var parseOptions = new CSharpParseOptions(languageVersion);

		IEnumerable<string> sources = caller is null
			? [Context, GlobalUsings]
			: [Context, GlobalUsings, caller];

		var compilation = CSharpCompilation.Create(
			assemblyName: "RequiredRequestMember",
			syntaxTrees: sources.Select(p => CSharpSyntaxTree.ParseText(p, parseOptions)),
			references: [
				.. Net100.References.All,
				MetadataReference.CreateFromFile(typeof(SQuiLBaseDataContext).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			],
			options: new CSharpCompilationOptions(
				OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable));

		var driver = CSharpGeneratorDriver
			.Create([new SQuiLGenerator(true).AsSourceGenerator()], parseOptions: parseOptions)
			.AddAdditionalTexts([new AdditionalQuery(Query)]);

		var ran = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

		var generated = ran.GetRunResult().GeneratedTrees;
		return (generated, output);
	}
}
