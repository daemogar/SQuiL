using System.Linq;

using SQuiL.Dialects;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

namespace SQuiL.Tests.Dialects;

// Mirrors SqlServerDialectTestHelper.cs, but tokenizes under SqliteDialect so SQLite-only type
// keywords (INTEGER/BLOB/BOOLEAN/GUID) and dialect-gated ones (REAL/DATE) resolve correctly.
//
// NOTE (Task 4/5 ordering — see task-4-brief.md): real SQLite `.squil` files use `Create Temp
// Table` headers (Task 5's parser work), not T-SQL `Declare`. Until Task 5 lands, this helper
// parses the ordinary `Declare`-form block instead, purely to exercise the tokenizer's SQLite
// type recognition under test here — switch to Create-Temp-Table parsing once Task 5 ships.
internal static class SqliteDialectTestHelper
{
	/// <summary>Parses SQL and returns the first scalar INPUT block (<c>@Param_&lt;name&gt;</c>).</summary>
	public static CodeBlock ParseSingleInputScalar(string sql)
	{
		var dialect = new SqliteDialect();
		var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, dialect));
		return blocks.First(b => b.CodeType == CodeType.INPUT_ARGUMENT);
	}

	/// <summary>Parses SQL and returns the first INPUT table/object block.</summary>
	public static CodeBlock ParseSingleInputBlock(string sql)
	{
		var dialect = new SqliteDialect();
		var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, dialect));
		return blocks.First(b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT
			&& b.CodeType != CodeType.INPUT_ARGUMENT);
	}
}
