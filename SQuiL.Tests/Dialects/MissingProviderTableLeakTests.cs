namespace SQuiL.Tests.Dialects;

using Microsoft.CodeAnalysis;

using System.Linq;

using Xunit;

/// <summary>
/// Regression coverage for a Critical finding against Task 3 (Phase 3B): a data context that
/// resolves to a dialect whose provider package is NOT referenced (SP0038) still ran its own
/// <c>SQuiLModel.Create</c> pass — which unconditionally registered every table-shaped
/// declaration into the compilation-wide <see cref="Microsoft.CodeAnalysis.SQuiLTableMap"/> via
/// <c>TableMap.Add(SQuiLTable)</c>, and <c>FileGenerator.GenerateCode()</c> unconditionally
/// emitted every table the map knew about. So a missing-provider context declaring a
/// TABLE-shaped variable could (1) still get its own table type emitted, breaking "only emission
/// is gated", and (2) poison a VALID, correctly-configured sibling context that happens to
/// declare a same-named table with a different shape — the sibling would fail to build with a
/// false-positive SP0017 (shape mismatch) caused solely by the broken context.
///
/// This compilation references SQuiL.SqlServer but NOT SQuiL.Sqlite: context A is an ordinary
/// SqlServer context (provider referenced) declaring <c>Widgets(WidgetID, Name)</c>; context B
/// explicitly targets Sqlite via <c>[SQuiLDialect(SQuiLDialect.Sqlite)]</c> (provider NOT
/// referenced -> SP0038) and declares a CONFLICTING <c>Widgets(WidgetID, Name, Extra)</c> shape.
/// </summary>
public class MissingProviderTableLeakTests
{
	private const string ContextA = """
		using Microsoft.Extensions.Configuration;
		using SQuiL;

		namespace TestCase;

		[SQuiLQueryAttribute(QueryFiles.WidgetsA)]
		public partial class WidgetsADataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration)
		{
		}
		""";

	private const string ContextB = """
		using Microsoft.Extensions.Configuration;
		using SQuiL;

		namespace TestCase;

		[SQuiLQueryAttribute(QueryFiles.WidgetsB)]
		[SQuiLDialect(SQuiLDialect.Sqlite)]
		public partial class WidgetsBDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration)
		{
		}
		""";

	private const string QueryA = """
		--Name: WidgetsA
		Declare @Returns_Widgets table(WidgetID int, Name varchar(50));
		Use [Db];
		Insert Into @Returns_Widgets Select WidgetID, Name From Widgets;
		Select * From @Returns_Widgets;
		""";

	// Conflicting shape: an extra `Extra int` column vs. context A's Widgets declaration above.
	private const string QueryB = """
		--Name: WidgetsB
		Declare @Returns_Widgets table(WidgetID int, Name varchar(50), Extra int);
		Use [Db];
		Insert Into @Returns_Widgets Select WidgetID, Name, Extra From Widgets;
		Select * From @Returns_Widgets;
		""";

	private static GeneratorDriverRunResult Run()
		=> TestHelper.RunForDiagnosticsAndSources(
			[ContextA, ContextB],
			[QueryA, QueryB],
			includeSqlServer: true,
			includeSqlite: false);

	[Fact]
	public void MissingProvider_context_reports_SP0038()
	{
		var result = Run();

		Assert.Contains(result.Diagnostics, d => d.Id == "SP0038");
	}

	[Fact]
	public void Valid_sibling_context_is_not_poisoned_by_missing_provider_shape_conflict()
	{
		var result = Run();

		// The bug: context B's conflicting Widgets(WidgetID, Name, Extra) shape used to leak into
		// the shared TableMap and collide with context A's Widgets(WidgetID, Name) -> false-positive
		// SP0017. Fixed: context B's tables never register, so no shape conflict is possible.
		Assert.DoesNotContain(result.Diagnostics, d => d.Id is "SP0017" or "SP0021");
	}

	[Fact]
	public void MissingProvider_context_emits_no_table_source_and_valid_sibling_keeps_its_own_shape()
	{
		var result = Run();

		var generatedTrees = result.Results
			.SelectMany(r => r.GeneratedSources)
			.ToList();

		// Context B (missing provider) must not get its Request/Response/DataContext files.
		Assert.DoesNotContain(generatedTrees, t => t.HintName.Contains("WidgetsBRequest"));
		Assert.DoesNotContain(generatedTrees, t => t.HintName.Contains("WidgetsBResponse"));
		Assert.DoesNotContain(generatedTrees, t => t.HintName.Contains("WidgetsBDataContext"));

		// The shared Widgets table type must be emitted exactly once, carrying ONLY context A's
		// shape (WidgetID, Name) -- no `Extra` column leaked in from context B.
		var widgetsSources = generatedTrees
			.Where(t => t.HintName.Contains("Widgets") && !t.HintName.Contains("WidgetsA") && !t.HintName.Contains("WidgetsB"))
			.ToList();

		Assert.Single(widgetsSources);
		var widgetsText = widgetsSources[0].SourceText.ToString();
		Assert.Contains("WidgetID", widgetsText);
		Assert.Contains("Name", widgetsText);
		Assert.DoesNotContain("Extra", widgetsText);
	}
}
