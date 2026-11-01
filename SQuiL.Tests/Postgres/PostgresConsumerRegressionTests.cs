using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using SQuiL.Dialects;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using Xunit;

namespace SQuiL.Tests.Postgres;

/// <summary>
/// Regressions found by the first real PostgreSQL consumer (family-todo, SQuiL 1.0.0-beta.215):
/// a Postgres-only project (no Microsoft.Data.SqlClient) got no code, a header that starts with a
/// comment was read as all body, and a context naming a missing query file stopped the run before
/// the TableType enum was emitted.
/// </summary>
public class PostgresConsumerRegressionTests
{
	// The consumer's file as it sits on disk: the `--Name:` line stays in the text (the test
	// harness's AdditionalQuery strips it, which is why the snapshot tests never caught this).
	const string GetDbStatus = """
		--Name: GetDbStatus
		-- Health check: proves the app can reach PostgreSQL and that migrations ran.
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

	static System.Collections.Generic.List<CodeBlock> Parse(string sql, ISqlDialect dialect)
		=> SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, dialect), dialect);

	[Theory]
	[InlineData("--Name: X\n-- a description\n")]
	[InlineData("/* a block comment */\n")]
	[InlineData("-- before\n/* multi\n   line */\n-- after\n")]
	public void Leading_comments_do_not_end_the_postgres_header(string comments)
	{
		var blocks = Parse(comments + """
			Create Temp Table Return_ServerVersion (ServerVersion text Not Null);
			-- between declarations
			Create Temp Table Return_MigrationCount (MigrationCount int4 Not Null);
			Select ServerVersion From Return_ServerVersion;
			""", new PostgresDialect());

		Assert.Equal(["ServerVersion", "MigrationCount"],
			blocks.Where(b => b.CodeType == CodeType.OUTPUT_VARIABLE).Select(b => b.Name));
		var body = Assert.Single(blocks, b => b.CodeType == CodeType.BODY);
		Assert.Equal("Select ServerVersion From Return_ServerVersion;", body.Name);
	}

	[Fact]
	public void Leading_comments_do_not_end_the_sqlite_header()
	{
		var blocks = Parse("""
			--Name: X
			Create Temp Table Return_Total (Total INTEGER);
			Select Total From Return_Total;
			""", new SqliteDialect());

		Assert.Single(blocks, b => b.CodeType == CodeType.OUTPUT_VARIABLE && b.Name == "Total");
	}

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
		Assert.DoesNotContain("--Name:", dataContext);
		Assert.Contains("string ServerVersion", response);
		Assert.Contains("int MigrationCount", response);
		Assert.Contains(sources, s => s.HintName.EndsWith("Constructor.g.cs")
			&& s.SourceText.ToString().Contains(": PostgresDataContext"));
	}

	[Fact]
	public void Context_naming_a_missing_query_file_still_emits_the_TableType_enum()
	{
		var result = TestHelper.RunForDiagnosticsAndSources([Context], [],
			includeSqlServer: false, includeSqlite: false, includePostgres: true,
			includeSqlClient: false, rawFiles: [new RawFile("Queries/GetDbStatus.squil", GetDbStatus)]);

		Assert.Contains(result.Results.SelectMany(r => r.GeneratedSources),
			s => s.HintName.EndsWith("TableTypeEnum.g.cs"));
	}

	sealed class RawFile(string path, string text) : AdditionalText
	{
		public override string Path { get; } = path;

		public override SourceText? GetText(System.Threading.CancellationToken cancellationToken = default)
			=> SourceText.From(text);
	}
}
