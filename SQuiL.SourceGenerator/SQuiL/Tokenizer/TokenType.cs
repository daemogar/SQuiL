namespace SQuiL.Tokenizer;

/// <summary>
/// Classifies every lexeme produced by <see cref="SQuiLTokenizer"/>.  Values are grouped into
/// numeric ranges (keywords 1000–1999, literals 2000–2999, types 3000–3999, symbols 4000–4999,
/// comments 5000–5999) so the parser can match an entire family with a single range check.
/// </summary>
public enum TokenType
{
	/// <summary>Sentinel value returned when the token stream is exhausted.</summary>
	END = 0,
	/// <summary>A SQL variable reference: <c>@Name</c>.</summary>
	VARIABLE = 1,
	/// <summary>An unquoted or bracket-quoted identifier: <c>Name</c> or <c>[Name]</c>.</summary>
	IDENTIFIER = 2,
	/// <summary>The raw SQL query body that follows the <c>USE</c> statement.</summary>
	BODY = 3,
	/// <summary>An <c>INSERT INTO @table</c> usage detected inside the query body.</summary>
	INSERT_INTO_TABLE = 4,
	/// <summary>A <c>SELECT @variable =</c> usage detected inside the query body.</summary>
	SELECT_VARIABLE = 5,
	// Keywords (1000–1999)
	/// <summary>Base value for all keyword tokens.</summary>
	KEYWORD = 1000,
	/// <summary><c>DECLARE</c> keyword.</summary>
	KEYWORD_DECLARE = KEYWORD + 1,
	/// <summary><c>USE</c> keyword.</summary>
	KEYWORD_USE = KEYWORD + 2,
	/// <summary><c>AS</c> keyword.</summary>
	KEYWORD_AS = KEYWORD + 11,
	/// <summary>Base value for keywords that are consumed but otherwise ignored by the parser.</summary>
	KEYWORD_IGNORED = KEYWORD + 100,
	/// <summary><c>INSERT</c> keyword (ignored by the block parser).</summary>
	KEYWORD_INSERT = KEYWORD_IGNORED + 1,
	/// <summary><c>INTO</c> keyword (ignored by the block parser).</summary>
	KEYWORD_INTO = KEYWORD_IGNORED + 2,
	/// <summary><c>VALUES</c> keyword (ignored by the block parser).</summary>
	KEYWORD_VALUES = KEYWORD_IGNORED + 3,
	/// <summary><c>SET</c> keyword (ignored by the block parser).</summary>
	KEYWORD_SET = KEYWORD_IGNORED + 4,
	// Literals (2000–2999)
	/// <summary>Base value for all literal tokens.</summary>
	LITERAL = 2000,
	/// <summary><c>NULL</c> literal.</summary>
	LITERAL_NOT_NULL = LITERAL + 1,
	/// <summary><c>NOT NULL</c> literal.</summary>
	LITERAL_NULL = LITERAL + 2,
	/// <summary>A single-quoted string literal.</summary>
	LITERAL_STRING = LITERAL + 11,
	/// <summary>An integer or numeric literal.</summary>
	LITERAL_NUMBER = LITERAL + 12,
	// Types (3000–3999) — see https://learn.microsoft.com/en-us/sql/t-sql/data-types/data-types-transact-sql
	/// <summary>Base value for all type tokens.</summary>
	TYPE = 3000,
	/// <summary><c>bit</c> — maps to C# <c>bool</c>.</summary>
	TYPE_BOOLEAN = TYPE + 1,
	/// <summary><c>int</c> — maps to C# <c>int</c>.</summary>
	TYPE_INT = TYPE + 2,
	/// <summary><c>decimal</c> — maps to C# <c>decimal</c>.</summary>
	TYPE_DECIMAL = TYPE + 4,
	/// <summary><c>varchar</c> / <c>nvarchar</c> — maps to C# <c>string</c>.</summary>
	TYPE_STRING = TYPE + 5,
	/// <summary><c>date</c> — maps to C# <c>System.DateOnly</c>.</summary>
	TYPE_DATE = TYPE + 6,
	/// <summary><c>time</c> — maps to C# <c>System.TimeOnly</c>.</summary>
	TYPE_TIME = TYPE + 7,
	/// <summary><c>datetime</c> / <c>datetime2</c> / <c>datetimeoffset</c> — maps to C# <c>System.DateTime</c>.</summary>
	TYPE_DATETIME = TYPE + 8,
	/// <summary><c>uniqueidentifier</c> — maps to C# <c>System.Guid</c>.</summary>
	TYPE_GUID = TYPE + 9,
	/// <summary><c>float</c> — maps to C# <c>double</c>.</summary>
	TYPE_FLOAT = TYPE + 11,
	/// <summary><c>double</c> — maps to C# <c>double</c>.</summary>
	TYPE_DOUBLE = TYPE + 12,
	/// <summary><c>binary(n)</c> — maps to C# <c>byte[]</c>.</summary>
	TYPE_BINARY = TYPE + 13,
	/// <summary><c>varbinary(max)</c> — maps to C# <c>byte[]</c>.</summary>
	TYPE_VARBINARY = TYPE + 14,
	/// <summary>Synthetic type indicating a <c>table(...)</c> variable (multi-row).</summary>
	TYPE_TABLE = TYPE + 21,
	/// <summary>Synthetic type indicating a <c>table(...)</c> variable used as a single object.</summary>
	TYPE_OBJECT = TYPE + 22,
	/// <summary>A built-in SQL function call such as <c>GETDATE()</c>.</summary>
	TYPE_FUNCTIONS = TYPE + 31,
	/// <summary><c>identity</c> column specifier.</summary>
	TYPE_IDENTITY = TYPE + 101,
	/// <summary><c>default</c> value specifier.</summary>
	TYPE_DEFAULT = TYPE + 102,
	// Symbols (4000–4999)
	/// <summary>Base value for all symbol tokens.</summary>
	SYMBOL = 4000,
	/// <summary><c>=</c></summary>
	SYMBOL_EQUAL = SYMBOL + '=',
	/// <summary><c>,</c></summary>
	SYMBOL_COMMA = SYMBOL + ',',
	/// <summary><c>(</c></summary>
	SYMBOL_LPREN = SYMBOL + '(',
	/// <summary><c>)</c></summary>
	SYMBOL_RPREN = SYMBOL + ')',
	// Comments (5000–5999)
	/// <summary>Base value for all comment tokens.</summary>
	COMMENT = 5000,
	/// <summary>A <c>-- …</c> single-line comment.</summary>
	COMMENT_SINGLELINE = COMMENT + 1,
	/// <summary>A <c>/* … */</c> multi-line comment.</summary>
	COMMENT_MULTILINE = COMMENT + 2
};
