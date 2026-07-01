using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

namespace SQuiL.Tests.ParamSharding;

/// <summary>
/// Minimal parser wiring for param-sharding unit tests.
/// Uses the real <see cref="SQuiLTokenizer"/> + <see cref="SQuiLParser"/> pipeline
/// to produce <see cref="CodeBlock"/>s from raw SQL strings, so test inputs are
/// as close as possible to what the generator actually processes.
/// </summary>
public static class ParserTestSupport
{
	/// <summary>
	/// Tokenizes and parses <paramref name="sql"/>, then returns the first
	/// <see cref="CodeBlock"/> that represents a table or object input parameter
	/// (i.e. <see cref="CodeType.INPUT_TABLE"/> or <see cref="CodeType.INPUT_OBJECT"/>).
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown when no table/object input block is found in the parsed SQL.
	/// </exception>
	public static CodeBlock FirstInputBlock(string sql)
	{
		var tokens = SQuiLTokenizer.GetTokens(sql);
		var blocks = SQuiLParser.ParseTokens(tokens);
		return blocks.First(b =>
			b.CodeType == CodeType.INPUT_TABLE ||
			b.CodeType == CodeType.INPUT_OBJECT);
	}
}
