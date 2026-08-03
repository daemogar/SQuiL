using System.Linq;

using SQuiL.Dialects;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

namespace SQuiL.Tests.Dialects;

// Mirrors SqliteDialectTestHelper.cs, but tokenizes/parses under PostgresDialect — PostgreSQL is
// a temp-table-header dialect (near-twin of SQLite; see PostgresDialect's class summary), so it
// shares the exact same `Create Temp Table <Prefix>_<Name> (...)` header grammar (Task 5's model),
// unchanged here in Task 4. Passes the dialect through to both the tokenizer (so PostgreSQL-only
// type keywords resolve — see PostgresTypeRegex/PostgresMultiWordTypeRegex in SQuiLTokenizer) and
// SQuiLParser.ParseTokens (so the single-column-object-collapses-to-scalar rule applies).
internal static class PostgresDialectTestHelper
{
	/// <summary>Parses SQL and returns the first scalar INPUT block (<c>Param_&lt;name&gt;</c>).</summary>
	public static CodeBlock ParseSingleInputScalar(string sql)
	{
		var dialect = new PostgresDialect();
		var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, dialect), dialect);
		return blocks.First(b => b.CodeType == CodeType.INPUT_ARGUMENT);
	}

	/// <summary>Parses SQL and returns the first OUTPUT table/list block (<c>Returns_&lt;name&gt;</c>).</summary>
	public static CodeBlock ParseSingleOutputTable(string sql)
	{
		var dialect = new PostgresDialect();
		var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, dialect), dialect);
		return blocks.First(b => (b.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT
			&& b.CodeType != CodeType.OUTPUT_VARIABLE);
	}

	/// <summary>Parses SQL and returns the first INPUT table/object block (mirrors
	/// <c>SqliteDialectTestHelper.ParseSingleInputBlock</c>).</summary>
	public static CodeBlock ParseSingleInputBlock(string sql)
	{
		var dialect = new PostgresDialect();
		var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, dialect), dialect);
		return blocks.First(b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT
			&& b.CodeType != CodeType.INPUT_ARGUMENT);
	}
}
