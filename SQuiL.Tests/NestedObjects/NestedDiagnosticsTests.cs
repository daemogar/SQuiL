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
///   SP0033 — two table/object blocks both declare `Primary Key` on the same key name (duplicate-pk).
///   SP0034 — following Primary-Key/Foreign-Key links forms a cycle.
/// Both diagnostics come from <see cref="SQuiL.Models.SQuiLKeyGraph.Errors"/>, reported by
/// <c>Microsoft.CodeAnalysis.FileGenerator.Create</c>. The generator run is inspected directly
/// (mirroring <c>TransactionDiagnosticTests.DebugRollbackWithoutDebugDoesNotEmitSP0026AtBuildTime</c>)
/// rather than via full snapshot comparison, since only the diagnostic Id matters here.
///
/// HISTORY (containment-direction feature, Ruling R2): declaration-order edge orientation landed in
/// Task 1 with the OLD PK-oriented ambiguity check (a child's column matching more than one table's
/// Primary Key) deleted outright, and cycle detection left structurally unreachable (every edge now
/// points from the earlier-declared block to the later one). Task 2 (this task, Ruling R0)
/// reintroduces SP0033 under an entirely NEW condition — not "a child matches 2+ parents' PKs", but
/// "two blocks both declare `Primary Key` on the same key name" — see
/// <see cref="TwoBlocksDeclaringTheSameKeyNameReportsSP0033"/> below. Task 3 (multi-container
/// resolution, which inverts edges) is what makes cycles reachable again and may reopen SP0034.
/// </summary>
public class NestedDiagnosticsTests
{
	private static ImmutableArray<Diagnostic> Run(string name, string sql)
	{
		var source = TestHelper.TestHeaderPublic([name]);

		var syntaxTree = CSharpSyntaxTree.ParseText(source);

		IEnumerable<MetadataReference> metareferences = [
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location),
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

	/// <summary>
	/// SP0033 (Ruling R0, this task) — "A" and "B" both declare `Primary Key` on the SAME key name
	/// ("SharedID"). A key name identifies one relationship and must have exactly one "one" side, so
	/// the second declaration ("B") is a build error. Note this is the SAME fixture Task 1 left
	/// negatively asserted (no SP0033) under the OLD "ambiguous parent" reading — the meaning of
	/// SP0033 changed, and this fixture now happens to satisfy the NEW condition too (two blocks,
	/// not a child matching two parents), so the assertion flips to positive.
	/// </summary>
	[Fact]
	public void TwoBlocksDeclaringTheSameKeyNameReportsSP0033()
	{
		var name = nameof(TwoBlocksDeclaringTheSameKeyNameReportsSP0033);
		var diagnostics = Run(name, """
			Declare @Returns_A table(SharedID int Primary Key, N int);
			Declare @Returns_B table(SharedID int Primary Key, M int);
			Declare @Returns_C table(CID int, SharedID int);
			Use [Db];
			Select 1;
			""");

		var sp0033 = diagnostics.Where(d => d.Id == "SP0033").ToList();
		var diagnostic = Assert.Single(sp0033);
		var message = diagnostic.GetMessage();
		Assert.Contains("`B` (line 2)", message);   // second declaration
		Assert.Contains("`A` (line 1)", message);    // first declaration
		Assert.Contains("declares `Primary Key` on the same key name as", message);
	}

	/// <summary>
	/// SP0033 fires exactly once per SECOND (and later) claimant — a third block declaring the same
	/// key name reports its OWN finding against the first owner, not a cascade of pairwise findings.
	/// </summary>
	[Fact]
	public void ThreeBlocksDeclaringTheSameKeyNameReportsSP0033TwiceAgainstTheFirst()
	{
		var name = nameof(ThreeBlocksDeclaringTheSameKeyNameReportsSP0033TwiceAgainstTheFirst);
		var diagnostics = Run(name, """
			Declare @Returns_A table(SharedID int Primary Key, N int);
			Declare @Returns_B table(SharedID int Primary Key, M int);
			Declare @Returns_C table(SharedID int Primary Key, P int);
			Use [Db];
			Select 1;
			""");

		var sp0033 = diagnostics.Where(d => d.Id == "SP0033").ToList();
		Assert.Equal(2, sp0033.Count);
		Assert.All(sp0033, d => Assert.Contains("`A`", d.GetMessage()));
	}

	/// <summary>
	/// SP0034 — A links to B via BID and B links back to A via AID. Under the OLD PK-oriented
	/// algorithm this was a two-node cycle. Under declaration-order orientation (Task 1, Ruling R2)
	/// every edge points from the earlier-declared block to the later one, so `childOf[Child] =
	/// Parent` always strictly decreases declaration order — a cycle can no longer form from this
	/// fixture. The cycle-detection code itself is retained (not deleted); it simply finds nothing
	/// here. Task 3 (multi-container resolution, which inverts edges) is what makes cycles reachable
	/// again and may reopen SP0034.
	/// </summary>
	[Fact]
	public void PrimaryForeignKeyCycleDoesNotReportSP0034()
	{
		var name = nameof(PrimaryForeignKeyCycleDoesNotReportSP0034);
		var diagnostics = Run(name, """
			Declare @Return_A table(AID int Primary Key, BID int);
			Declare @Return_B table(BID int Primary Key, AID int);
			Use [Db];
			Select 1;
			""");

		var sp0034 = diagnostics.Where(d => d.Id == "SP0034").ToList();
		Assert.Empty(sp0034);
	}
}
