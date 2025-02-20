using Microsoft.CodeAnalysis;

using System.Text;
using System.Text.RegularExpressions;

namespace SQuiL.Tokenizer;

public class SQuiLTokenizer(string Text)
{
	private string Text { get; } = Text;

	private static Regex KeywordRegex { get; } = new(
		"""^(DECLARE|SET|USE|AS|INSERT|INTO|VALUES)\s""", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

	private static Regex TypeRegex { get; } = new(
		"""^(bit|int|float|double|decimal(|\(\d,\d\))|uniqueidentifier|(date(?!time)|time|datetime(|2|offset))|n?(text|(var)?char\s*\(\s*(\d+|max)\s*\))|table\s*\(|identity(\s*\(\s*\d+\s*,\s*\d+\s*\))?|default\s+(\d+|'.*?')|(varbinary\s*\(\s*max\s*\)|binary\s*\(\s*\d+\s*\)\s*))""", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

	private static Regex FunctionRegex { get; } = new(
		"""^(getdate\(\))""", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

	private static Regex SymbolRegex { get; } = new(
		"""^(;|=|,|\(|\)|--|/\*)""", RegexOptions.Compiled | RegexOptions.Singleline);

	private static Regex NumberRegex { get; } = new(
		"""^(\d+)""", RegexOptions.Compiled | RegexOptions.Singleline);

	private static Regex IdentifierRegex { get; } = new(
		"""^(@?([A-Z][A-Z0-9_]*)|\[([A-Z][A-Z0-9_]*)\]|__SQuiL__Type__)""", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

	private int Line { get; set; } = 1;

	private int Column { get; set; } = 1;

	private int _index = 0;
	private int Index { get => _index; }

	private char Letter => Word?.Length > 0 ? Word[0] : '\0';

	private string Word => Index < Text.Length ? Text[Index..] : "";

	private List<Token>? Tokens { get; set; }

	public static List<Token> GetTokens(string text)
		=> new SQuiLTokenizer(text).GetTokens();

	public List<Token> GetTokens()
	{
		if (Tokens is not null)
			return Tokens;
		Tokens = [];

		var token = default(Token);
		while (Index < Text.Length)
		{
			WhileWhiteSpace();

			if (UseStatement())
				break;

			if (Keyword())
				continue;
			if (Type())
				continue;
			if (Symbol())
				continue;
			if (Function())
				continue;
			if (Literal())
				continue;
			if (Identifier())
				continue;

			WhileWhiteSpace();
			if (Word.Length > 0)
				Throw("Input");
		}

		return Tokens;

		Token T(TokenType type, string? original, string value = default!)
			=> new(type, Index, value)
			{
				Original = original
			};

		bool Literal()
		{
			if (NullLiteral())
				return true;
			if (StringLiteral())
				return true;
			if (NumberLiteral())
				return true;
			return false;

			bool NullLiteral()
			{
				if (!Word.StartsWith("null", StringComparison.CurrentCultureIgnoreCase))
					return false;

				Tokens.Add(T(TokenType.LITERAL_NULL, Word));
				Increment(4);

				return true;
			}

			bool StringLiteral()
			{
				if (Letter != '\'')
					return false;

				var line = Line;
				var column = Column;
				var value = "";

				Increment();

				while ((Letter != '\'' && Letter != '\0') || Word.StartsWith("''"))
				{
					if (Word.StartsWith("''"))
						Increment();

					value += Letter;
					Increment();
				}

				if (Letter != '\'')
					throw DE(value.Length + 1, line, column,
						$"String Literal Missing End Quote `'`");

				Tokens.Add(T(TokenType.LITERAL_STRING, $"'{value}'", value));
				Increment();

				return true;
			}

			bool NumberLiteral()
			{
				var number = NumberRegex.Match(Word);
				if (!number.Success)
					return false;

				Tokens.Add(T(TokenType.LITERAL_NUMBER, Word, number.Value));
				Increment(number.Value.Length);

				return true;
			}
		}

		bool Function() => Try(FunctionRegex, p =>
		{
			return T(TokenType.TYPE_FUNCTIONS, p.Value, p.Value.ToUpper());
		});

		bool Symbol() => Try(SymbolRegex, p =>
		{
			var value = p.Value + "   ";

			switch (value[0])
			{
				case ';':
					Increment();
					return default(Token);
				case ',':
					return T(TokenType.SYMBOL_COMMA, p.Value);
				case '(':
					return T(TokenType.SYMBOL_LPREN, p.Value);
				case ')':
					return T(TokenType.SYMBOL_RPREN, p.Value);
				case '=':
					return T(TokenType.SYMBOL_EQUAL, p.Value);
				case '-':
					switch (value[1])
					{
						case '-':
							string text = Word;
							var index = text.IndexOf('\n');

							if (index >= 0)
								text = text[..index];

							Increment(text.Length - 2);

							var trimmed = text[2..].Trim().TrimStart('-').TrimStart();

							return T(TokenType.COMMENT_SINGLELINE, trimmed, text);
					}
					break;
				case '/':
					switch (value[1])
					{
						case '*':
							string text = Word;
							var index = Word.IndexOf("*/");

							if (index >= 0)
								text = text[..(index + 2)];

							Increment(text.Length - 2);

							var trimmed = '*' + text[2..^2].Trim().TrimStart('*').TrimStart();

							return T(TokenType.COMMENT_MULTILINE, text, trimmed);
					}
					break;
			}

			return $"Invalid Symbol: `{p.Value}`";
		});

		bool Type() => Try(TypeRegex, p =>
		{
			var value = p.Value.ToLower().Split('(', ')');

			switch (value[0].Trim())
			{
				case "bit":
					return T(TokenType.TYPE_BOOLEAN, p.Value);
				case "int":
					return T(TokenType.TYPE_INT, p.Value);
				case "double" or "float":
					return T(TokenType.TYPE_DOUBLE, "float");
				case "decimal":
					var decimalParts = p.Value.Split(',');
					if (decimalParts.Length != 2 && decimalParts.Any(q => !int.TryParse(q, out var r) || r > 0))
						return $"Invalid Decimal Values: `{p.Value}`";
					return T(TokenType.TYPE_DECIMAL, p.Value);
				case "uniqueidentifier":
					return T(TokenType.TYPE_GUID, p.Value);
				case "char":
				case "nchar":
				case "varchar":
				case "nvarchar":
					return T(TokenType.TYPE_STRING, p.Value,
						Value().Replace("max", "4096"));
				case "binary":
					return T(TokenType.TYPE_BINARY, p.Value, Value());
				case "varbinary":
					return T(TokenType.TYPE_VARBINARY, p.Value, "max");
				case "text":
				case "ntext":
					return T(TokenType.TYPE_STRING, p.Value, p.Value);
				case "date":
					return T(TokenType.TYPE_DATE, p.Value);
				case "time":
					return T(TokenType.TYPE_TIME, p.Value);
				case "datetime":
				case "datetime2":
				case "datetimeoffset":
					return T(TokenType.TYPE_DATETIME, p.Value);
				case "identity":
					return T(TokenType.TYPE_IDENTITY, p.Value,
						value.Length == 1 ? "1,1" : Value());
				case "table":
					Tokens.Add(T(TokenType.TYPE_TABLE, p.Value));
					return T(TokenType.SYMBOL_LPREN, default);
				default:
					return $"Invalid Type: `{p.Value}`";
			}

			string Value()
			{
				var text = new StringBuilder(value[1].Length);
				for (var i = 0; i < value[1].Length; i++)
				{
					if (char.IsWhiteSpace(value[1][i]))
						continue;

					text.Append(value[1][i]);
				}
				return text.ToString().ToLower();
			}
		});

		bool Keyword() => Try(KeywordRegex, p =>
		{
			if (Enum.TryParse<TokenType>($"KEYWORD_{p.Value}", true, out var type))
				return T(type, p.Value);

			return $"Invalid Keyword: `{0}`";
		});

		bool Identifier() => Try(IdentifierRegex, p =>
		{
			var type = TokenType.IDENTIFIER;
			var value = p.Value;

			if (value.StartsWith("@"))
			{
				type = TokenType.VARIABLE;
				value = value[1..];
			}
			else if (value.StartsWith("["))
			{
				value = value[1..^1];
			}

			return T(type, p.Value, value);
		});

		bool UseStatement()
		{
			if (token?.Type != TokenType.KEYWORD_USE)
				return false;

			var name = Identifier();
			if (!name)
				Throw("Identifier");

			WhileWhiteSpace();

			var word = Word.Trim();

			Tokens.AddRange(GetTokens());

			Tokens.Add(T(TokenType.BODY, word, word));

			return true;

			IEnumerable<Token> GetTokens()
			{
				SQuiLTokenizer tokenizer = new(word);
				while (tokenizer.Index < tokenizer.Text.Length)
				{
					if (IsInsertInto(out var insertIntoToken))
						yield return insertIntoToken;
					if (IsSelectVariable(out var selectVariable))
						yield return selectVariable;

					tokenizer.Increment();
				}

				bool IsSelectVariable(out Token selectVariable)
				{
					selectVariable = default!;

					if (SkipIfNot("select"))
						return false;
					SkipWhitespace();

					var offset = tokenizer.Index;

					if (SkipIfNot("@", false))
						return false;

					var variableName = tokenizer.Word;
					var match = IdentifierRegex.Match(variableName);
					if (!match.Success)
						return false;

					SkipWhitespace();
					if (!SkipIfNot("="))
						return false;

					tokenizer.Increment(match.Value.Length);

					selectVariable = new Token(
						TokenType.SELECT_VARIABLE,
						offset,
						match.Value[1..]);

					return true;
				}

				bool IsInsertInto(out Token insertIntoToken)
				{
					insertIntoToken = default!;

					if (SkipIfNot("insert"))
						return false;
					SkipWhitespace();
					if (SkipIfNot("into"))
						return false;
					SkipWhitespace();
					if (SkipIfNot("@", false))
						return false;

					var tableName = tokenizer.Word;
					var match = IdentifierRegex.Match(tableName);
					if (!match.Success)
						return false;

					tokenizer.Increment(match.Value.Length);
					var index = tokenizer.Index + 1;
					SkipWhitespace();

					if (tokenizer.Letter == '(')
						return false;

					insertIntoToken = new Token(
						TokenType.INSERT_INTO_TABLE,
						index,
						match.Value[1..]);

					return true;
				}

				bool SkipIfNot(string search, bool increment = true)
				{
					if (!tokenizer.Word.StartsWith(search, StringComparison.InvariantCultureIgnoreCase))
						return true;

					if (increment)
						tokenizer.Increment(search.Length);

					return false;
				}

				void SkipWhitespace()
				{
					while (char.IsWhiteSpace(tokenizer.Letter))
						tokenizer.Increment();
				}
			}
		}

		void WhileWhiteSpace()
		{
			while (char.IsWhiteSpace(Letter) || Letter == ';')
			{
				Increment();

				if (Letter == '\n')
				{
					Line++;
					Column = 1;
				}
			}
		}

		bool Try(Regex regex, Func<Match, ExceptionOrValue<Token?>> callback)
		{
			var match = regex.Match(Word);
			if (!match.Success)
				return false;

			if (!callback(match).TryGetValue(out token, out var exception))
				throw DE(match.Length, exception.Message);

			if (token is not null)
				Tokens.Add(token);

			Increment(match.Length);

			return true;
		}

		void Throw(string type)
		{
			var length = Word.IndexOfAny([' ', '\t', '\r', '\n']);
			if (length < 1)
				throw DE(Word.Length, $"Invalid {type}: `{Word}`");
			throw DE(length, $"Invalid {type}: `{Word[..length]}`");
		}
	}

	private void Increment(int value = 1)
	{
		_index += value;
		Column += value;
	}

	private DiagnosticException DE(int length, string message)
		=> DE(length, Line, Column, message);

	private DiagnosticException DE(int length, int line, int column, string message)
		=> new(Index, length, line, column, Line, Column, message);
}