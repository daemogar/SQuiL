namespace SQuiL.Tests.ExtensionParsing;

using SQuiL.SsmsExtension.Parsing;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Regression coverage for the Task 1 (containment-direction feature) review round 2 fix:
/// <c>SQuiLLinter</c>'s key-graph builder previously filtered out any block linked to more than
/// one container (the old PK-oriented <c>distinctParents.Count &gt; 1</c> check), which
/// guaranteed <c>KeyGraph.Edges</c> never held two entries with the same <c>Child</c> — so
/// <c>LintOneKeyGraph</c>'s <c>graph.Edges.ToDictionary(e =&gt; e.Child, e =&gt; e.Parent)</c> was
/// safe. Porting R1 (declaration-order orientation) into <c>BuildKeyGraph</c> correctly deleted
/// that filter (ambiguity handling is deferred to Task 2 — see Ruling R2), but left the
/// downstream <c>.ToDictionary</c> in place, which throws <c>ArgumentException</c> on the
/// duplicate <c>Child</c> key a multi-container fixture now produces. Because <c>LintKeyGraph</c>
/// is called unguarded from <c>Lint(...)</c>, which <c>SQuiLErrorTagger.cs</c> calls with no
/// try/catch, this crashed tag recomputation for both SSMS and Visual Studio — and skipped every
/// lint pass scheduled after <c>LintKeyGraph</c> in <c>Lint(...)</c>
/// (LintParamsBeforeReturns/LintOrphanContext/LintMutationDiagnostics/LintDebugRollbackHint).
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
	/// The exact SP0033 fixture already used elsewhere in the suite (matches
	/// NestedDiagnosticsTests.ChildMatchingTwoPrimaryKeysDoesNotReportSP0033 and
	/// KeyGraphTests.ChildMatchingTwoPrimaryKeysIsNotAmbiguousError): C carries a column
	/// ("SharedID") matching the declared Primary Key of both A and B, so C is linked to TWO
	/// containers (A and B) — the multi-container/ambiguity case Ruling R2 leaves reachable
	/// (only the same-pair reciprocal-key case from C1 is deduped away).
	/// </summary>
	private const string MultiContainerSql = """
		Declare @Returns_A table(SharedID int Primary Key, N int);
		Declare @Returns_B table(SharedID int Primary Key, M int);
		Declare @Returns_C table(CID int, SharedID int);
		Use [Db]; Select 1;
		""";

	[Fact]
	public void LintDoesNotThrowWhenAChildLinksToTwoContainers()
	{
		var diagnostics = new List<SQuiLDiagnostic>();

		var exception = Record.Exception(() => SQuiLLinter.Lint(MultiContainerSql, diagnostics));

		Assert.Null(exception);
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

		var childCounts = new Dictionary<SQuiLVariable, int>();
		foreach (var edge in graph.Edges)
			childCounts[edge.Child] = childCounts.TryGetValue(edge.Child, out var n) ? n + 1 : 1;
		Assert.Contains(childCounts, kv => kv.Value > 1);

		var diagnostics = new List<SQuiLDiagnostic>();
		var exception = Record.Exception(() => SQuiLLinter.LintKeyGraph(MultiContainerSql, diagnostics));
		Assert.Null(exception);
	}
}
