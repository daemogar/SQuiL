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
/// </summary>
public class SQuiLLinterKeyGraphTests
{
	/// <summary>
	/// C carries BOTH "AID" (A's Primary Key) and "BID" (B's Primary Key) — two DIFFERENT key
	/// columns, each matching a DIFFERENT single-owner Primary Key — so C is a genuine child of
	/// TWO containers (A and B). This is NOT a duplicate-pk violation (A and B declare Primary Key
	/// on two DIFFERENT names), so SP0033 must stay silent; it is the dual-parent/repeated-Child
	/// case the crash-safety fix below guards against.
	/// </summary>
	private const string MultiContainerSql = """
		Declare @Returns_A table(AID int Primary Key, N int);
		Declare @Returns_B table(BID int Primary Key, M int);
		Declare @Returns_C table(CID int, AID int, BID int);
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
	/// The narrower, more direct repro: <c>BuildKeyGraph</c> alone must produce an <c>Edges</c>
	/// list with a repeated <c>Child</c> (C appears twice — once linked from A, once from B) for
	/// this fixture, proving the scenario the fix guards against is genuinely reachable (not
	/// filtered out upstream), then that <c>LintKeyGraph</c> built directly on top of it does not
	/// throw either.
	/// </summary>
	[Fact]
	public void BuildKeyGraphProducesRepeatedChildAndLintKeyGraphStillDoesNotThrow()
	{
		var parsed = SQuiLParser.Parse(MultiContainerSql, EditorDialect.SqlServer);
		var outputList = SQuiLLinter.OutputTableVariables(parsed);
		var graph = SQuiLLinter.BuildKeyGraph(outputList);

		Assert.Empty(graph.DuplicatePrimaryKeys);

		var childCounts = new Dictionary<SQuiLVariable, int>();
		foreach (var edge in graph.Edges)
			childCounts[edge.Child] = childCounts.TryGetValue(edge.Child, out var n) ? n + 1 : 1;
		Assert.Contains(childCounts, kv => kv.Value > 1);

		var diagnostics = new List<SQuiLDiagnostic>();
		var exception = Record.Exception(() => SQuiLLinter.LintKeyGraph(MultiContainerSql, diagnostics));
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
}
