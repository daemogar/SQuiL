using Microsoft.CodeAnalysis;

using SQuiL.Generator;
using SQuiL.Models;
using SQuiL.Tokenizer;

namespace SQuiL.SourceGenerator.Parser;

/// <summary>
/// Transforms a flat list of <see cref="Token"/>s produced by <see cref="SQuiLTokenizer"/> into
/// a structured list of <see cref="CodeBlock"/>s that the model builders can consume.
/// Recognises <c>DECLARE</c>, <c>USE</c>, and the query body, and classifies each block
/// with the appropriate <see cref="CodeType"/> flags.
/// </summary>
/// <param name="Tokens">The token list to parse, typically returned by <see cref="SQuiLTokenizer.GetTokens()"/>.</param>
public class SQuiLParser(List<Token> Tokens)
{
	private int Index { get; set; }

	/// <summary>Advances the cursor by <paramref name="count"/> positions, skipping comment tokens.</summary>
	private void Consume(int count = 1)
	{
		Increment(count);

		while (Current.Type == TokenType.COMMENT_SINGLELINE
			|| Current.Type == TokenType.COMMENT_MULTILINE)
			Increment(1);

		void Increment(int i) => Index = Math.Min(Index + i, Tokens.Count);
	}

	/// <summary>The token at the current cursor position.</summary>
	private Token Current => Peek(0);

	/// <summary>Returns the token at <c>Index + offset</c>, or <see cref="Token.END"/> if past the end.</summary>
	private Token Peek(int offset)
		=> Index + offset < Tokens.Count
			? Tokens[Index + offset]
			: Token.END;

	private List<CodeBlock>? CodeBlocks { get; set; }

	/// <summary>Static entry point: parses <paramref name="tokens"/> and returns the resulting code blocks.</summary>
	public static List<CodeBlock> ParseTokens(List<Token> tokens)
		=> new SQuiLParser(tokens).ParseTokens();

	/// <summary>
	/// Parses the token stream and returns all recognized <see cref="CodeBlock"/>s.
	/// Results are cached so repeated calls are free.
	/// </summary>
	public List<CodeBlock> ParseTokens()
	{
		if (CodeBlocks is not null) return CodeBlocks;
		CodeBlocks = [];

		var ignoring = false;
		var declaring = true;

		Func<Variable> ExpectVariable;
		{
			var resultIndex = 0;
			ExpectVariable = () =>
			{
				var token = Expect(TokenType.VARIABLE);
				var parts = token.Value.Split(['_'], 2);

				// NEW: only the four input specials are recognized; a bare @Error/@Errors is
				// no longer special and falls through to the unprefixed-declare error below.
				if (SQuiLGenerator.IsSpecial(parts[0]))
					return new(token, CodeType.INPUT_ARGUMENT, false, false, parts[0], IsSpecialDeclaration: true);

				if (parts[0].StartsWith("param", StringComparison.CurrentCultureIgnoreCase))
				{
					var type = CodeType.INPUT_ARGUMENT;
					var isTable = Current.Type == TokenType.TYPE_TABLE;
					if (isTable)
						type = "Ss".Contains(parts[0].Last()) ? CodeType.INPUT_TABLE : CodeType.INPUT_OBJECT;

					return new(token, type, type == CodeType.INPUT_OBJECT, type == CodeType.INPUT_TABLE, parts[1]);
				}

				if (parts[0].StartsWith("return", StringComparison.CurrentCultureIgnoreCase))
				{
					var type = CodeType.OUTPUT_VARIABLE;
					var isTable = Current.Type == TokenType.TYPE_TABLE;
					if (isTable)
						type = "Ss".Contains(parts[0].Last()) ? CodeType.OUTPUT_TABLE : CodeType.OUTPUT_OBJECT;

					return new(token, type, type == CodeType.OUTPUT_OBJECT, type == CodeType.OUTPUT_TABLE, parts[1]);
				}

				if (declaring) ThrowError();

				var name = parts.Length == 1 || parts[1].IsNullOrWhiteSpace()
					? $"Result{++resultIndex}"
					: parts[1];

				return new(token, CodeType.OUTPUT, false, Current.Type == TokenType.TYPE_TABLE, name);

				void ThrowError() => throw new DiagnosticException(
					$"Expected a declare with @Param_<variable or object name>, @Params_<tablename>, " +
					$"@Return_<variable or object name>, or/and @Returns_<tablename>, " +
					$"but found @{token.Value} instead.");
			};
		}

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
					declaring = false;
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

		// `Insert Into @Var` / `Select @Var` statements are now emitted verbatim as part of
		// the body — no output-side injection. Result sets are routed at runtime by their
		// column signature (shape key), so there is nothing to rewrite here; just advance.
		void ProcessInsertIntoTablesAndSelectVariables() => Consume();

		void ProcessBodyStatement()
		{
			CodeBlocks.Add(new(CodeType.BODY, Current with { Value = Current.Value }));
			Consume();
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

				if (variable.IsTable) Process(TokenType.TYPE_TABLE);
				else if (variable.IsObject) Process(TokenType.TYPE_OBJECT);
				else ProcessScaler();

				void ProcessScaler()
				{
					var type = Expect(TokenType.TYPE);

					bool? nullableMarker = null;
					if (Current.Type == TokenType.LITERAL_NULL) { nullableMarker = true; Consume(); }
					else if (Current.Type == TokenType.LITERAL_NOT_NULL) { nullableMarker = false; Consume(); }

					if (Current.Type != TokenType.SYMBOL_EQUAL)
					{
						CodeBlocks.Add(new(variable.Type,
							type with
							{
								Value = variable.Name,
								Original = $"{variable.Token.Original} {type.Original}"
							})
						{
							Size = type.Value,
							IsSpecialDeclaration = variable.IsSpecialDeclaration,
							IsNullableMarker = nullableMarker
						});

						return;
					}

					Consume();

					string? defaultValue = Current.Type switch
					{
						TokenType.LITERAL_NULL => default,
						TokenType.LITERAL_NOT_NULL => default,
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

					CodeBlocks.Add(new(variable.Type, type with
					{
						Original = $"{variable.Token.Original} {type.Original}"
					}, variable.Name, defaultValue)
					{
						Size = type.Value,
						IsSpecialDeclaration = variable.IsSpecialDeclaration,
						IsNullableMarker = nullableMarker
					});
				}

				void Process(TokenType type)
				{
					Consume();

					Expect(TokenType.SYMBOL_LPREN);
					CodeBlock block = new(variable.Type,
						new Token(type, offset, variable.Name)
						{
							Original = $"{variable.Token.Original} table"
						});

					do
					{
						if (Current.Type == TokenType.SYMBOL_COMMA)
							Consume();

						var identifier = Expect(TokenType.IDENTIFIER);
						CodeItem item = new(identifier, Expect(TokenType.TYPE));

						// Peel optional column modifiers in any order: null marker, Primary Key, default.
						while (true)
						{
							if (Current.Type == TokenType.LITERAL_NULL) { item = item with { IsNullable = true }; Consume(); }
							else if (Current.Type == TokenType.LITERAL_NOT_NULL) { item = item with { IsNullable = false }; Consume(); }
							else if (Current.Type == TokenType.TYPE_PRIMARY_KEY) { item = item with { IsPrimaryKey = true }; Consume(); }
							else if (Current.Type == TokenType.TYPE_DEFAULT) { item = item with { DefaultValue = Current.Value }; Consume(); }
							else break;
						}

						block.Properties.Add(item);
					}
					while (Current.Type == TokenType.SYMBOL_COMMA);

					Expect(TokenType.SYMBOL_RPREN);

					CodeBlocks.Add(block);
				}
			}
			while (Current.Type == TokenType.SYMBOL_COMMA);
		}
	}

	/// <summary>
	/// Consumes and returns the current token if it matches <paramref name="type"/> exactly,
	/// or if its integer value falls within the same thousand-range (i.e. the same token family).
	/// Throws a <see cref="DiagnosticException"/> on mismatch.
	/// </summary>
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

	/// <summary>Creates a position-less <see cref="DiagnosticException"/> with the given message.</summary>
	private DiagnosticException DE(string message) => new(message);
}

file record Variable(Token Token, CodeType Type, bool IsObject, bool IsTable, string Name, bool IsSpecialDeclaration = false);
