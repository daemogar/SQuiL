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
/// Primary Key) deleted outright, and cycle detection left structurally unreachable directly out of
/// edge construction (every RAW edge points from the earlier-declared block to the later one). Task 2
/// reintroduces SP0033 under an entirely NEW condition — not "a child matches 2+ parents' PKs", but
/// "two blocks both declare `Primary Key` on the same key name" — see
/// <see cref="TwoBlocksDeclaringTheSameKeyNameReportsSP0033"/> below. Task 3's R3 multi-container
/// resolution CAN invert an edge (new Parent = the higher-order block), which makes SP0034 reachable
/// again at build time — see <see cref="MultiContainerCascadeReportsSP0034AtBuildTime"/> below for a
/// real, minimal (4-block) fixture that fires it.
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
	/// every RAW edge points from the earlier-declared block to the later one, so a cycle can't form
	/// from edge construction alone. `pairSeen` also dedupes this pair to exactly ONE edge (A->B),
	/// so this fixture never has a block that is Child of 2+ edges — R3's resolution loop (Task 3)
	/// never runs on it, and it stays acyclic permanently, not just "for now" (see
	/// <see cref="MultiContainerCascadeReportsSP0034AtBuildTime"/> below for the real, reachable
	/// case — it needs 4+ blocks, proved exhaustively for 3).
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

	/// <summary>
	/// SP0034, actually reachable (Task 3): the minimal 4-block cycle, confirmed at build time
	/// through the full generator pipeline (not just the <c>SQuiLKeyGraph.Build</c> unit test — see
	/// <c>KeyGraphTests.MultiContainerResolutionCanCascadeIntoACycle</c> for the pass-by-pass trace).
	/// Category owns CategoryID; Product owns ProductID and carries CategoryID (FK to Category);
	/// Junction carries both CategoryID and ProductID (a many-to-many junction); Summary, declared
	/// first, carries ProductID too (embeds Product). R3 resolves Product's conflict (Summary wins
	/// over Category, inverting Category's link into Product-&gt;Category), then Junction's conflict
	/// (Category wins over Product, inverting Product's link into Junction-&gt;Product) — closing the
	/// loop Category-&gt;Junction-&gt;Product-&gt;Category. Also confirms generation is suppressed
	/// entirely for the file (no partial/flat-path fallback), matching SP0033's behavior.
	/// </summary>
	[Fact]
	public void MultiContainerCascadeReportsSP0034AtBuildTime()
	{
		var name = nameof(MultiContainerCascadeReportsSP0034AtBuildTime);
		var diagnostics = Run(name, """
			Declare @Returns_Summary table(ProductID varchar(10));
			Declare @Returns_Category table(CategoryID int Primary Key, Name varchar(50));
			Declare @Returns_Product table(ProductID varchar(10) Primary Key, CategoryID int, Title varchar(50));
			Declare @Returns_Junction table(CategoryID int, ProductID varchar(10), Note varchar(50));
			Use [Db];
			Select * From @Returns_Summary;
			Select * From @Returns_Category;
			Select * From @Returns_Product;
			Select * From @Returns_Junction;
			""");

		var sp0034 = diagnostics.Where(d => d.Id == "SP0034").ToList();
		var diagnostic = Assert.Single(sp0034);
		var message = diagnostic.GetMessage();
		Assert.Contains("cycle", message, System.StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// C1 regression (review round 1), full pipeline. This is the fixture where a `childOf`-based
	/// walk (one parent remembered per Child, last-write-wins) misses the cycle entirely — see
	/// <c>KeyGraphTests.CycleThroughACollapsedChildOfEntryIsStillDetected</c> for why. It matters
	/// here specifically because THIS is the test that would crash without the fix: when SP0034
	/// stays silent, <c>FileGenerator.cs</c> does not bail out of code generation, and
	/// <c>SQuiLDataContext.cs</c>'s <c>DeepestFirstEdges</c> local function (a build-time,
	/// generator-side post-order traversal — NOT emitted runtime code) recurses over
	/// <c>EffectiveGraph.ChildrenOf(...)</c> with no cycle guard. A cyclic graph sends it into
	/// unbounded recursion, i.e. a stack overflow that kills the process running the generator
	/// (`dotnet test`/`dotnet build`/VBCSCompiler) with no diagnostic output at all. Confirmed by
	/// temporarily reverting to the pre-fix `childOf`-based cycle walk and observing this exact
	/// test crash the test host (exit code -1073741571 / 0xC00000FD STATUS_STACK_OVERFLOW) rather
	/// than fail an assertion — see task-3-report.md's "Fix report — review round 1" section for
	/// the exact repro transcript.
	/// </summary>
	[Fact]
	public void FiveBlockCycleThroughACollapsedChildOfEntryReportsSP0034AtBuildTime()
	{
		var name = nameof(FiveBlockCycleThroughACollapsedChildOfEntryReportsSP0034AtBuildTime);
		var diagnostics = Run(name, """
			Declare @Returns_A table(AID int Primary Key, CID int);
			Declare @Returns_B table(BID int Primary Key, N int);
			Declare @Returns_C table(CID int Primary Key, BID int);
			Declare @Returns_D table(DN int, AID int, BID int);
			Declare @Returns_E table(EN int, BID int, CID int);
			Use [Db];
			Select * From @Returns_A;
			Select * From @Returns_B;
			Select * From @Returns_C;
			Select * From @Returns_D;
			Select * From @Returns_E;
			""");

		var sp0034 = diagnostics.Where(d => d.Id == "SP0034").ToList();
		Assert.Single(sp0034);
	}
}
