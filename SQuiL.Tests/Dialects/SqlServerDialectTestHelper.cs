using System.Linq;

using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

namespace SQuiL.Tests.Dialects;

internal static class SqlServerDialectTestHelper
{
	// Parses SQL and returns the first INPUT table/object block, for exercising
	// CodeBlock-shaped dialect methods without hand-constructing a CodeBlock.
	public static CodeBlock ParseSingleInputBlock(string sql)
	{
		var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql));
		return blocks.First(b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT
			&& b.CodeType != CodeType.INPUT_ARGUMENT);
	}
}
