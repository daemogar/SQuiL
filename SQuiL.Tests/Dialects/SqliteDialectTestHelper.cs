using System.Linq;

using SQuiL.Dialects;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

namespace SQuiL.Tests.Dialects;

// Mirrors SqlServerDialectTestHelper.cs, but tokenizes under SqliteDialect so SQLite-only type
// keywords (INTEGER/BLOB/BOOLEAN/GUID) and dialect-gated ones (REAL/DATE) resolve correctly.
//
// Parses the real SQLite header form — `Create Temp Table <Prefix>_<Name> (...)` (Task 5) —
// not T-SQL `Declare`/`Use`. Passes the dialect through to SQuiLParser.ParseTokens as well as
// the tokenizer, since the SQLite single-column-object-collapses-to-scalar rule lives there.
internal static class SqliteDialectTestHelper
{
	/// <summary>Parses SQL and returns the first scalar INPUT block (<c>Param_&lt;name&gt;</c>).</summary>
	public static CodeBlock ParseSingleInputScalar(string sql)
	{
		var dialect = new SqliteDialect();
		var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, dialect), dialect);
		return blocks.First(b => b.CodeType == CodeType.INPUT_ARGUMENT);
	}

	/// <summary>Parses SQL and returns the first INPUT table/object block.</summary>
	public static CodeBlock ParseSingleInputBlock(string sql)
	{
		var dialect = new SqliteDialect();
		var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, dialect), dialect);
		return blocks.First(b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT
			&& b.CodeType != CodeType.INPUT_ARGUMENT);
	}
}
