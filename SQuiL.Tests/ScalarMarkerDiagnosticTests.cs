using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SQuiL.Generator;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace SQuiL.Tests;

/// <summary>
/// SP0037 — a standalone <c>null</c>/<c>not null</c> marker on a scalar Declare is invalid
/// T-SQL (Declare doesn't support nullability modifiers). Use an <c>= null</c> initializer to
/// make the scalar nullable instead. Mirrors <c>NestedObjects.NestedDiagnosticsTests</c>'s
/// explicit-inspection style — only the diagnostic Id/message matters here, not full snapshots.
/// </summary>
public class ScalarMarkerDiagnosticTests
{
	private static ImmutableArray<Diagnostic> Run(string name, string sql)
	{
		var source = TestHelper.TestHeaderPublic([name]);

		var syntaxTree = CSharpSyntaxTree.ParseText(source);

		IEnumerable<MetadataReference> metareferences = [
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location)
		];

		var compilation = CSharpCompilation.Create(
			assemblyName: "Tests",
			references: metareferences,
			syntaxTrees: [syntaxTree]);

		var additionalFile = (AdditionalText)new AdditionalQuery($"""
			--Name: {name}
			{sql}
			""");

		var generator = new SQuiLGenerator(true);
		var driver = CSharpGeneratorDriver.Create(generator);
		driver = (CSharpGeneratorDriver)driver.AddAdditionalTexts([additionalFile]);
		driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

		return driver.GetRunResult().Diagnostics;
	}

	[Fact]
	public void ScalarNullMarkerEmitsSP0037()
	{
		var diagnostics = Run(nameof(ScalarNullMarkerEmitsSP0037), """
			--Name: ScalarNullMarkerEmitsSP0037
			Declare @Param_X int null;
			Declare @Return_Count int;
			Use MyDb;
			Select @Return_Count = 1;
			""");
		var sp = diagnostics.Where(d => d.Id == "SP0037").ToList();
		Assert.Single(sp);
		Assert.Equal(DiagnosticSeverity.Error, sp[0].Severity);
		Assert.Contains("= null", sp[0].GetMessage());
	}

	[Fact]
	public void ScalarNotNullMarkerEmitsSP0037()
	{
		var diagnostics = Run(nameof(ScalarNotNullMarkerEmitsSP0037), """
			--Name: ScalarNotNullMarkerEmitsSP0037
			Declare @Param_X int not null;
			Declare @Return_Count int;
			Use MyDb;
			Select @Return_Count = 1;
			""");
		Assert.Single(diagnostics.Where(d => d.Id == "SP0037"));
	}

	[Fact]
	public void ScalarNullInitializerDoesNotEmitSP0037()
	{
		var diagnostics = Run(nameof(ScalarNullInitializerDoesNotEmitSP0037), """
			--Name: ScalarNullInitializerDoesNotEmitSP0037
			Declare @Param_X int = null;
			Declare @Return_Count int;
			Use MyDb;
			Select @Return_Count = 1;
			""");
		Assert.Empty(diagnostics.Where(d => d.Id == "SP0037"));
	}
}
