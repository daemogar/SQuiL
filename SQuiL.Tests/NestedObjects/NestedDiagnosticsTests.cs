namespace SQuiL.Tests.NestedObjects;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SQuiL.Generator;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

/// <summary>
/// Verifies the two build-time key-graph diagnostics:
///   SP0033 — a nested-object child's column matches more than one declared Primary Key (ambiguous parent).
///   SP0034 — following Primary-Key/Foreign-Key links forms a cycle.
/// Both diagnostics come from <see cref="SQuiL.Models.SQuiLKeyGraph.Errors"/>, reported by
/// <c>Microsoft.CodeAnalysis.FileGenerator.Create</c>. The generator run is inspected directly
/// (mirroring <c>TransactionDiagnosticTests.DebugRollbackWithoutDebugDoesNotEmitSP0026AtBuildTime</c>)
/// rather than via full snapshot comparison, since only the diagnostic Id matters here.
/// </summary>
public class NestedDiagnosticsTests
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

	/// <summary>SP0033 — "C" carries a column ("SharedID") matching the Primary Key of both "A" and "B".</summary>
	[Fact]
	public void ChildMatchingTwoPrimaryKeysReportsSP0033()
	{
		var name = nameof(ChildMatchingTwoPrimaryKeysReportsSP0033);
		var diagnostics = Run(name, """
			Declare @Returns_A table(SharedID int Primary Key, N int);
			Declare @Returns_B table(SharedID int Primary Key, M int);
			Declare @Returns_C table(CID int, SharedID int);
			Use [Db];
			Select 1;
			""");

		var sp0033 = diagnostics.Where(d => d.Id == "SP0033").ToList();
		Assert.Single(sp0033);
		Assert.Equal(DiagnosticSeverity.Error, sp0033[0].Severity);
		Assert.Contains("C", sp0033[0].GetMessage());
	}

	/// <summary>SP0034 — A links to B via BID and B links back to A via AID, forming a cycle.</summary>
	[Fact]
	public void PrimaryForeignKeyCycleReportsSP0034()
	{
		var name = nameof(PrimaryForeignKeyCycleReportsSP0034);
		var diagnostics = Run(name, """
			Declare @Return_A table(AID int Primary Key, BID int);
			Declare @Return_B table(BID int Primary Key, AID int);
			Use [Db];
			Select 1;
			""");

		var sp0034 = diagnostics.Where(d => d.Id == "SP0034").ToList();
		Assert.Single(sp0034);
		Assert.Equal(DiagnosticSeverity.Error, sp0034[0].Severity);
	}
}
