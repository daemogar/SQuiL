using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace SQuiL.Tests.Postgres;

/// <summary>
/// Regressions found by the first real PostgreSQL consumer (family-todo, SQuiL 1.0.0-beta.215):
/// a Postgres-only project (no Microsoft.Data.SqlClient) got no code.
/// </summary>
public class PostgresConsumerRegressionTests
{
	// The consumer's first query (family-todo's DB status check).
	const string GetDbStatus = """
		Create Temp Table Return_ServerVersion (ServerVersion text Not Null);
		Create Temp Table Return_MigrationCount (MigrationCount int4 Not Null);

		Insert Into Return_ServerVersion (ServerVersion) Select version();
		Insert Into Return_MigrationCount (MigrationCount) Select Count(*)::int4 From schema_migrations;

		Select ServerVersion From Return_ServerVersion;
		Select MigrationCount From Return_MigrationCount;
		""";

	const string Context = """
		using SQuiL;

		namespace FamilyTodo.Data;

		[SQuiLDialect(SQuiLDialect.Postgres)]
		[SQuiLQuery(QueryFiles.GetDbStatus, setting: "Db")]
		public partial class FamilyTodoDataContext { }
		""";

	[Fact]
	public void Postgres_only_project_without_SqlClient_generates_the_context()
	{
		var result = TestHelper.RunForDiagnosticsAndSources([Context], [],
			includeSqlServer: false, includeSqlite: false, includePostgres: true,
			includeSqlClient: false, rawFiles: [new RawFile("GetDbStatus.squil", GetDbStatus)]);

		Assert.DoesNotContain(result.Diagnostics, d => d.Id is "SP0005" or "SP0007" or "SP0023");
		Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

		var sources = result.Results.SelectMany(r => r.GeneratedSources).ToList();
		var dataContext = Assert.Single(sources, s => s.HintName.EndsWith("GetDbStatusDataContext.g.cs")).SourceText.ToString();
		var response = Assert.Single(sources, s => s.HintName.EndsWith("GetDbStatusResponse.g.cs")).SourceText.ToString();

		Assert.Contains("SQuiLResultType<GetDbStatusResponse>", dataContext);
		Assert.Contains("ExecuteReaderAsync", dataContext);
		Assert.Contains("string ServerVersion", response);
		Assert.Contains("int MigrationCount", response);
		Assert.Contains(sources, s => s.HintName.EndsWith("Constructor.g.cs")
			&& s.SourceText.ToString().Contains(": PostgresDataContext"));
	}

	sealed class RawFile(string path, string text) : AdditionalText
	{
		public override string Path { get; } = path;

		public override SourceText? GetText(System.Threading.CancellationToken cancellationToken = default)
			=> SourceText.From(text);
	}
}
