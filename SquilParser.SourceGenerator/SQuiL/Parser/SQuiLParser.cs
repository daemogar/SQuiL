using Microsoft.CodeAnalysis;

using SQuiL.Models;
using SQuiL.Tokenizer;

using System.Text;

namespace SquilParser.SourceGenerator.Parser;

public class SQuiLParser(List<Token> Tokens)
{
	private int Index { get; set; }
	private void Consume(int count = 1)
	{
		Increment(count);

		while (Current.Type == TokenType.COMMENT_SINGLELINE
			|| Current.Type == TokenType.COMMENT_MULTILINE)
			Increment(1);

		void Increment(int i) => Index = Math.Min(Index + i, Tokens.Count);
	}

	private Token Current => Peek(0);
	private Token Next => Peek(1);

	private Token Peek(int offset)
		=> Index + offset < Tokens.Count
			? Tokens[Index + offset]
			: Token.END;

	private List<CodeBlock>? CodeBlocks { get; set; }

	public static List<CodeBlock> ParseTokens(List<Token> tokens)
		=> new SQuiLParser(tokens).ParseTokens();

	public List<CodeBlock> ParseTokens()
	{
		if (CodeBlocks is not null) return CodeBlocks;
		CodeBlocks = [];

		Func<Variable> ExpectVariable;
		{
			var resultIndex = 0;
			ExpectVariable = () =>
			{
				var token = Expect(TokenType.VARIABLE);
				var parts = token.Value.Split(['_'], 2);

				if (!parts[0].StartsWith("return", StringComparison.CurrentCultureIgnoreCase))
					return new(token, CodeType.INPUT, Current.Type == TokenType.TYPE_TABLE, parts[0]);

				string name;
				if (parts.Length == 1 || parts[1].IsNullOrWhiteSpace())
					name = $"Result{++resultIndex}";
				else
					name = parts[1];

				return new(token, CodeType.OUTPUT, Current.Type == TokenType.TYPE_TABLE, name);
			};
		}

		List<(Variable Variable, CodeBlock CodeBlock)> tablesAndVariables = [];
		List<(TokenType Type, CodeBlock CodeBlock, Token InsertIntoTable)> injectables = [];

		var ignoring = false;
		while (Current != Token.END)
		{
			switch (Current.Type)
			{
				case TokenType.KEYWORD_DECLARE:
					ignoring = false;
					ProcessDeclareStatement();
					continue;
				case TokenType.KEYWORD_USE:
					ignoring = false;
					ProcessUseStatement();
					continue;
				case TokenType.INSERT_INTO_TABLE:
				case TokenType.SELECT_VARIABLE:
					ignoring = false;
					ProcessInsertIntoTablesAndSelectVariables();
					continue;
				case TokenType.BODY:
					ignoring = false;
					ProcessBodyStatement();
					continue;
				default:
					if (!ignoring)
						ignoring = IgnoreTokenType();
					break;
			}

			if (ignoring)
				Consume();
			else
				throw DE($"Invalid `{Current}`");
		}

		return CodeBlocks;

		bool IgnoreTokenType()
		{
			var diff = (int)Current.Type;
			diff |= (int)TokenType.KEYWORD_IGNORED;
			return diff > 0;
		}

		void ProcessInsertIntoTablesAndSelectVariables()
		{
			foreach (var (variable, table) in tablesAndVariables)
			{
				if (variable.Token.Value != Current.Value)
					continue;

				injectables.Add((Current.Type, table, Current));

				break;
			}

			Consume();
		}

		void ProcessBodyStatement()
		{
			var body = Current.Value;
			List<string> injected = [];

			foreach (var (type, name, table, offset) in injectables
				.Select(p => (p.Type, p.InsertIntoTable.Value, p.CodeBlock, Offset: p.InsertIntoTable.Offset - 1))
				.OrderByDescending(p => p.Offset))
			{
				if (injected.Contains(name))
					continue;

				injected.Add(name);

				body = body[0..offset] + type switch
				{
					TokenType.INSERT_INTO_TABLE => $"({string.Join(", ", table.Table
						.Where(q => q.Identifier is not null).Select(q => q.Identifier.Value))})",
					TokenType.SELECT_VARIABLE => T(name),
					_ => throw new DiagnosticException(
						$"Invalid code block `{type}`")
				} + body[offset..];
			}

			StringBuilder sb = new();
			sb.AppendLine(body);

			foreach (var (table, code) in tablesAndVariables
				.Where(p => !p.Variable.IsTable
					&& p.CodeBlock.CodeType == CodeType.OUTPUT_VARIABLE))
			{
				var name = table.Token.Original![1..];

				if (injected.Contains(name))
					continue;

				injected.Add(name);

				sb.AppendLine()
					.AppendLine($"Select{T(name)} {table.Token.Original}");
			}

			CodeBlocks.Add(new(CodeType.BODY, Current with
			{
				Value = sb.ToString()
			}));

			Consume();

			string T(string name)
				=> $" '{name}' As [{SQuiLDataContext.SQuiLTableTypeDatabaseTagName}],";
		}

		void ProcessUseStatement()
		{
			Consume();
			var identifier = Expect(TokenType.IDENTIFIER);
			CodeBlocks.Add(new(CodeType.USING, identifier));
		}

		void ProcessDeclareStatement()
		{
			do
			{
				int offset = Current.Offset;

				Consume();

				var variable = ExpectVariable();
				if (variable.IsTable)
				{
					var code = variable.Type | CodeType.TABLE;
					var type = Current;
					Consume();

					Expect(TokenType.SYMBOL_LPREN);
					CodeBlock block = new(code,
						new Token(TokenType.TYPE_TABLE, offset, variable.Name)
						{
							Original = $"{variable.Token.Original} table"
						});

					do
					{
						if (Current.Type == TokenType.SYMBOL_COMMA)
							Consume();

						var identifier = Expect(TokenType.IDENTIFIER);
						CodeItem item = new(identifier, Expect(TokenType.TYPE));

						if (Current.Type == TokenType.LITERAL_NULL)
						{
							item = item with { IsNullable = true };
							Consume();
						}

						block.Table.Add(item);
					}
					while (Current.Type == TokenType.SYMBOL_COMMA);

					Expect(TokenType.SYMBOL_RPREN);

					CodeBlocks.Add(block);

					tablesAndVariables.Add((variable, block));
				}
				else
				{
					CodeBlock codeBlock = default!;

					try
					{
						var code = variable.Type | CodeType.ARGUMENT;
						var type = Expect(TokenType.TYPE);

						if (Current.Type != TokenType.SYMBOL_EQUAL)
						{
							codeBlock = new(code,
								type with
								{
									Value = variable.Name,
									Original = $"{variable.Token.Original} {type.Original}"
								})
							{
								Size = type.Value
							};
							CodeBlocks.Add(codeBlock);
							continue;
						}

						Consume();

						string? defaultValue = Current.Type switch
						{
							TokenType.LITERAL_NULL => default,
							TokenType.LITERAL_STRING => Current.Value,
							TokenType.LITERAL_NUMBER => Current.Value,
							TokenType.TYPE_FUNCTIONS => Current.Value switch
							{
								"GETDATE()" => "System.DateOnly.FromDateTime(System.DateTime.Now)",
								_ => throw DE($"Unsupported SQL Function `{Current.Value}` for variable {variable.Name}.")
							},
							_ => throw DE($"Unsupported Token type `{Current.Type}` for variable {variable.Name}.")
						};

						Consume();

						CodeBlocks.Add(codeBlock = new(code, type with
						{
							Original = $"{variable.Token.Original} {type.Original}"
						}, variable.Name, defaultValue)
						{
							Size = type.Value
						});
					}
					finally
					{
						if (codeBlock is not null)
							tablesAndVariables.Add((variable, codeBlock));
					}
				}
			}
			while (Current.Type == TokenType.SYMBOL_COMMA);
		}
	}

	private Token Expect(TokenType type, TokenType range)
		=> Expect(type, (int)range - (int)type);

	private Token Expect(TokenType type, int range = 1000)
	{
		if (Current.Type == type)
			return ConsumeToken();

		var vType = (int)((double)type / 1000) * 1000;
		if (type == TokenType.END || vType <= 0)
			throw DE($"Expected token type `{type:G}` but found `{Current.Expect()}`");

		var cType = (int)Current.Type;
		if (cType > vType && cType < vType + range)
			return ConsumeToken();

		throw DE($"Expected a token `{type:G}` but found `{Current.Expect()}`");

		Token ConsumeToken()
		{
			var token = Current;
			Consume();
			return token;
		}
	}

	private DiagnosticException DE(string message) => new(message);
}

file record Variable(Token Token, CodeType Type, bool IsTable, string Name);
