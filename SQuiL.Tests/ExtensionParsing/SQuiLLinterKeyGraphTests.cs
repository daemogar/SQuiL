namespace SQuiL.Tests.ExtensionParsing;

using SQuiL.SsmsExtension.Parsing;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// Regression coverage for the Task 1 (containment-direction feature) review round 2 fix:
/// <c>SQuiLLinter</c>'s key-graph builder previously filtered out any block linked to more than
/// one container (the old PK-oriented <c>distinctParents.Count &gt; 1</c> check), which
/// guaranteed <c>KeyGraph.Edges</c> never held two entries with the same <c>Child</c> — so a naive
/// <c>graph.Edges.ToDictionary(e =&gt; e.Child, e =&gt; e.Parent)</c> was safe. Porting R1
/// (declaration-order orientation) into <c>BuildKeyGraph</c> correctly deleted that filter, which
/// made a genuinely-dual-parent child (one that carries TWO DIFFERENT key columns, each matching a
/// DIFFERENT single-owner Primary Key) reachable again — <c>LintOneKeyGraph</c> now builds
/// <c>childOf</c> with a manual last-write-wins loop specifically to stay safe under that. Because
/// <c>LintKeyGraph</c> is called unguarded from <c>Lint(...)</c>, which <c>SQuiLErrorTagger.cs</c>
/// calls with no try/catch, a regression here would crash tag recomputation for both SSMS and
/// Visual Studio — and skip every lint pass scheduled after <c>LintKeyGraph</c> in <c>Lint(...)</c>
/// (LintParamsBeforeReturns/LintOrphanContext/LintMutationDiagnostics/LintDebugRollbackHint).
///
/// NOTE (Task 2, Ruling R0): the fixture below intentionally uses two DIFFERENT key names (AID,
/// BID), NOT the SAME key name declared twice — Task 2 makes the latter case SP0033
/// ("duplicate-pk"; see NestedDiagnosticsTests.TwoBlocksDeclaringTheSameKeyNameReportsSP0033 and
/// KeyGraphTests.TwoBlocksSharingAKeyNameIsADuplicatePrimaryKeyError), which is a DIFFERENT
/// scenario from the dual-parent-via-different-keys crash-safety repro this file targets. Before
/// Task 2, this file used a same-key-name fixture (A and B both declaring `Primary Key` on
/// "SharedID"); that fixture now reports SP0033 and — because both PK claims funnel through a
/// single surviving owner — no longer reproduces a repeated Child, so it was replaced here with a
/// fixture that keeps the repeated-Child scenario reachable without tripping SP0033.
///
/// This is the first C# test coverage <c>SQuiLLinter.cs</c> has had in either extension — the
/// Parsing\ folder has zero VS SDK dependency (only System.*), so it's compiled directly into
/// this test assembly (see SQuiL.Tests.csproj's linked-Compile ItemGroup), the same pattern
/// already used for SQuiLVersion.cs (see ExtensionVersionTests.cs). The Visual Studio extension's
/// copy is required byte-identical modulo the namespace/using lines (CLAUDE.md); this project
/// only compiles the SSMS copy, so a future edit that touches only the VS copy is caught by that
/// byte-diff check, not by this test.
///
/// UPDATE (Task 3, R3 multi-container resolution): <c>MultiContainerSql</c> below — C carrying
/// two DIFFERENT keys (AID, BID), neither of which C owns — is exactly the "junction, mixed case"
/// R3 now resolves: A (declared first) keeps C; B's link is dropped and, because B owns the
/// dropped edge's key (BID), inverted into a NEW edge C-&gt;B. So this fixture no longer produces a
/// repeated <c>Child</c> in <c>graph.Edges</c> — it resolves to a clean two-edge chain (A-&gt;C,
/// C-&gt;B) — see <c>BuildKeyGraphResolvesTheDualParentJunctionIntoAChain</c> below (renamed from
/// the pre-R3 <c>BuildKeyGraphProducesRepeatedChildAndLintKeyGraphStillDoesNotThrow</c>, whose
/// assertion is no longer true). The genuinely-surviving repeated-<c>Child</c> case post-R3 is the
/// SHARED-LOOKUP scenario (2+ containers, ALL embedding the SAME owned key) — R3 allows that to
/// stand, so the crash-safety property (`childOf`'s last-write-wins construction) is now exercised
/// by <c>SharedLookupSql</c> / <c>BuildKeyGraphProducesRepeatedChildForASharedLookupAndDoesNotThrow</c>.
/// </summary>
public class SQuiLLinterKeyGraphTests
{
	/// <summary>
	/// C carries BOTH "AID" (A's Primary Key) and "BID" (B's Primary Key) — two DIFFERENT key
	/// columns, each matching a DIFFERENT single-owner Primary Key — so C is (pre-R3) a candidate
	/// child of TWO containers (A and B). This is NOT a duplicate-pk violation (A and B declare
	/// Primary Key on two DIFFERENT names), so SP0033 must stay silent. Post-R3 (Task 3), this
	/// resolves to a chain (A-&gt;C, C-&gt;B) rather than surviving as a repeated Child — see the
	/// class doc comment above.
	/// </summary>
	private const string MultiContainerSql = """
		Declare @Returns_A table(AID int Primary Key, N int);
		Declare @Returns_B table(BID int Primary Key, M int);
		Declare @Returns_C table(CID int, AID int, BID int);
		Use [Db]; Select 1;
		""";

	/// <summary>
	/// Shared lookup (Task 3, R3): Structure and Widget are both declared before Contact and both
	/// carry ContactID (Contact's own Primary Key) — R3's "all embed" exception allows Contact to
	/// keep BOTH containers, so this is the fixture that genuinely still produces a repeated
	/// <c>Child</c> in <c>graph.Edges</c> after R3 runs.
	/// </summary>
	private const string SharedLookupSql = """
		Declare @Returns_Structure table(Title varchar(50), ContactID varchar(10));
		Declare @Returns_Widget table(Label varchar(50), ContactID varchar(10));
		Declare @Returns_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));
		Use [Db]; Select 1;
		""";

	[Fact]
	public void LintDoesNotThrowWhenAChildLinksToTwoContainers()
	{
		var diagnostics = new List<SQuiLDiagnostic>();

		var exception = Record.Exception(() => SQuiLLinter.Lint(MultiContainerSql, diagnostics));

		Assert.Null(exception);
		Assert.DoesNotContain(diagnostics, d => d.Code == "SP0033");
	}

	/// <summary>
	/// Post-R3 (Task 3): <c>BuildKeyGraph</c> no longer leaves C with two competing containers —
	/// A (declared first) keeps C, and B's link is dropped and inverted (B owns the dropped edge's
	/// key, BID) into a new edge C-&gt;B. The result is a clean two-edge chain, not a repeated
	/// Child. <c>LintKeyGraph</c> built on top of it still must not throw.
	/// </summary>
	[Fact]
	public void BuildKeyGraphResolvesTheDualParentJunctionIntoAChain()
	{
		var parsed = SQuiLParser.Parse(MultiContainerSql, EditorDialect.SqlServer);
		var outputList = SQuiLLinter.OutputTableVariables(parsed);
		var graph = SQuiLLinter.BuildKeyGraph(outputList);

		Assert.Empty(graph.DuplicatePrimaryKeys);
		Assert.Equal(2, graph.Edges.Count);

		var childCounts = new Dictionary<SQuiLVariable, int>();
		foreach (var edge in graph.Edges)
			childCounts[edge.Child] = childCounts.TryGetValue(edge.Child, out var n) ? n + 1 : 1;
		Assert.All(childCounts, kv => Assert.Equal(1, kv.Value));

		var toC = Assert.Single(graph.Edges, e => e.Child.Name == "C");
		Assert.Equal("A", toC.Parent.Name);
		Assert.False(toC.IsEmbed);

		var toB = Assert.Single(graph.Edges, e => e.Child.Name == "B");
		Assert.Equal("C", toB.Parent.Name);
		Assert.True(toB.IsEmbed);

		var diagnostics = new List<SQuiLDiagnostic>();
		var exception = Record.Exception(() => SQuiLLinter.LintKeyGraph(MultiContainerSql, diagnostics));
		Assert.Null(exception);
	}

	/// <summary>
	/// The genuinely-surviving repeated-Child case post-R3: Contact legitimately keeps BOTH
	/// Structure and Widget as containers (R3's "all embed" shared-lookup exception), so
	/// <c>LintKeyGraph</c>'s manual last-write-wins <c>childOf</c> construction is the thing
	/// actually protecting against an <c>ArgumentException</c> here.
	/// </summary>
	[Fact]
	public void BuildKeyGraphProducesRepeatedChildForASharedLookupAndDoesNotThrow()
	{
		var parsed = SQuiLParser.Parse(SharedLookupSql, EditorDialect.SqlServer);
		var outputList = SQuiLLinter.OutputTableVariables(parsed);
		var graph = SQuiLLinter.BuildKeyGraph(outputList);

		Assert.Empty(graph.DuplicatePrimaryKeys);

		var childCounts = new Dictionary<SQuiLVariable, int>();
		foreach (var edge in graph.Edges)
			childCounts[edge.Child] = childCounts.TryGetValue(edge.Child, out var n) ? n + 1 : 1;
		Assert.Contains(childCounts, kv => kv.Value > 1);
		Assert.All(graph.Edges, e => Assert.True(e.IsEmbed));

		var diagnostics = new List<SQuiLDiagnostic>();
		var exception = Record.Exception(() => SQuiLLinter.LintKeyGraph(SharedLookupSql, diagnostics));
		Assert.Null(exception);
	}

	/// <summary>
	/// SP0033 (Ruling R0, Task 2): "A" and "B" both declare `Primary Key` on the SAME key name
	/// ("SharedID"). Editor-parity companion to
	/// NestedDiagnosticsTests.TwoBlocksDeclaringTheSameKeyNameReportsSP0033 — verifies
	/// <c>SQuiLLinter.LintKeyGraph</c> (the editor mirror) reports the same diagnostic with the
	/// same wording as the generator.
	/// </summary>
	[Fact]
	public void LintKeyGraphReportsSP0033ForDuplicatePrimaryKey()
	{
		const string sql = """
			Declare @Returns_A table(SharedID int Primary Key, N int);
			Declare @Returns_B table(SharedID int Primary Key, M int);
			Declare @Returns_C table(CID int, SharedID int);
			Use [Db]; Select 1;
			""";

		var diagnostics = new List<SQuiLDiagnostic>();
		SQuiLLinter.LintKeyGraph(sql, diagnostics);

		var sp0033 = diagnostics.Where(d => d.Code == "SP0033").ToList();
		var diagnostic = Assert.Single(sp0033);
		Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
		Assert.Contains("`B`", diagnostic.Message);
		Assert.Contains("`A`", diagnostic.Message);
		Assert.Contains("declares `Primary Key` on the same key name as", diagnostic.Message);
	}

	/// <summary>
	/// SP0034, actually reachable (Task 3): editor-parity companion to
	/// NestedDiagnosticsTests.MultiContainerCascadeReportsSP0034AtBuildTime — the SAME minimal
	/// 4-block cycle fixture, verifying <c>SQuiLLinter.LintKeyGraph</c> squiggles it too. See that
	/// generator test's doc comment (and KeyGraphTests.MultiContainerResolutionCanCascadeIntoACycle)
	/// for the full pass-by-pass R3 trace.
	/// </summary>
	[Fact]
	public void LintKeyGraphReportsSP0034ForAMultiContainerCascade()
	{
		const string sql = """
			Declare @Returns_Summary table(ProductID varchar(10));
			Declare @Returns_Category table(CategoryID int Primary Key, Name varchar(50));
			Declare @Returns_Product table(ProductID varchar(10) Primary Key, CategoryID int, Title varchar(50));
			Declare @Returns_Junction table(CategoryID int, ProductID varchar(10), Note varchar(50));
			Use [Db]; Select 1;
			""";

		var diagnostics = new List<SQuiLDiagnostic>();
		SQuiLLinter.LintKeyGraph(sql, diagnostics);

		var sp0034 = diagnostics.Where(d => d.Code == "SP0034").ToList();
		var diagnostic = Assert.Single(sp0034);
		Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
		Assert.Contains("cycle", diagnostic.Message, System.StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// C1 regression (review round 1): editor-parity companion to
	/// NestedDiagnosticsTests.FiveBlockCycleThroughACollapsedChildOfEntryReportsSP0034AtBuildTime /
	/// KeyGraphTests.CycleThroughACollapsedChildOfEntryIsStillDetected — the SAME 5-block fixture
	/// where a `childOf`-based walk collapses B's TWO valid embed parents (C and D) down to one and
	/// misses the C-&gt;B-&gt;E-&gt;C cycle that runs through the discarded one.
	/// </summary>
	[Fact]
	public void LintKeyGraphReportsSP0034ForACycleThroughACollapsedChildOfEntry()
	{
		const string sql = """
			Declare @Returns_A table(AID int Primary Key, CID int);
			Declare @Returns_B table(BID int Primary Key, N int);
			Declare @Returns_C table(CID int Primary Key, BID int);
			Declare @Returns_D table(DN int, AID int, BID int);
			Declare @Returns_E table(EN int, BID int, CID int);
			Use [Db]; Select 1;
			""";

		var diagnostics = new List<SQuiLDiagnostic>();
		SQuiLLinter.LintKeyGraph(sql, diagnostics);

		var sp0034 = diagnostics.Where(d => d.Code == "SP0034").ToList();
		Assert.Single(sp0034);
	}

	/// <summary>
	/// C2 regression (review round 1): editor-parity companion to
	/// KeyGraphTests.MultiContainerResolutionReachesTheTrueFixedPointBeyondTheOldGuardBound — the
	/// SAME 5-block fixture where the OLD `list.Count + 1` guard bound cut resolution off before C
	/// reached its true single-parent fixed point.
	/// </summary>
	[Fact]
	public void BuildKeyGraphReachesTheTrueFixedPointBeyondTheOldGuardBound()
	{
		const string sql = """
			Declare @Returns_A table(AID int Primary Key, N int);
			Declare @Returns_B table(BID int Primary Key, AID int);
			Declare @Returns_C table(CID int Primary Key, AID int, BID int);
			Declare @Returns_D table(DN int, AID int, BID int);
			Declare @Returns_E table(EN int, AID int, BID int, CID int);
			Use [Db]; Select 1;
			""";

		var parsed = SQuiLParser.Parse(sql, EditorDialect.SqlServer);
		var outputList = SQuiLLinter.OutputTableVariables(parsed);
		var graph = SQuiLLinter.BuildKeyGraph(outputList);

		Assert.Empty(graph.DuplicatePrimaryKeys);
		Assert.Equal(4, graph.Edges.Count);
		Assert.All(graph.Edges, e => Assert.Equal("A", e.Parent.Name));
		Assert.All(graph.Edges, e => Assert.False(e.IsEmbed));
	}

	/// <summary>
	/// I3 regression (review round 1): Contact's Primary Key IS linked (both Structure and Widget
	/// embed it) — SP0035 must NOT fire just because Contact is never a `Parent` (the old, wrong
	/// orphan predicate). Editor-parity companion to
	/// KeyGraphTests.SharedLookupAllowsOnePkOwnerInManyContainers's Hints assertion.
	/// </summary>
	[Fact]
	public void LintKeyGraphDoesNotReportSP0035ForASharedLookupsPrimaryKey()
	{
		var diagnostics = new List<SQuiLDiagnostic>();
		SQuiLLinter.LintKeyGraph(SharedLookupSql, diagnostics);

		Assert.DoesNotContain(diagnostics, d => d.Code == "SP0035");
	}
}
