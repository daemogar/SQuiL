using Microsoft.CodeAnalysis;
using Microsoft.Data.SqlClient;

namespace SQuiL.Tokenizer;

/// <summary>
/// Represents a single lexeme produced by <see cref="SQuiLTokenizer"/>.
/// Carries the classified <see cref="TokenType"/>, the character offset in the source,
/// the normalized value, and (optionally) the verbatim original text.
/// </summary>
/// <param name="Type">The classified token type.</param>
/// <param name="Offset">Character offset of the token's first character in the source text.</param>
/// <param name="Value">Normalized value (e.g. trimmed, case-adjusted, or stripped of delimiters).</param>
public record Token(TokenType Type, int Offset, string Value)
{
	/// <summary>The verbatim source text for this token, preserved for diagnostic and SQL-emit purposes.</summary>
	public string? Original { get; init; }

	/// <summary>Creates a token with no value (used for punctuation tokens whose value is implicit).</summary>
	public Token(TokenType Type, int Offset) : this(Type, Offset, default!) { }

	/// <summary>Sentinel token returned when the token stream is exhausted.</summary>
	public static Token END { get; } = new(TokenType.END, -1);

	/// <summary>Returns a human-readable description of this token, used in parse-error messages.</summary>
	public string Expect() => $"{Type:G} => {Value}";

	/// <summary>Returns the <c>reader.GetXxx</c> method fragment used to read this token's SQL type from a <see cref="System.Data.Common.DbDataReader"/>.</summary>
	public string DataReader() => "reader." + Type switch
	{
		TokenType.TYPE_BOOLEAN => nameof(SqlDataReader.GetBoolean),
		TokenType.TYPE_INT => nameof(SqlDataReader.GetInt32),
		TokenType.TYPE_FLOAT => nameof(SqlDataReader.GetFloat),
		TokenType.TYPE_DOUBLE => nameof(SqlDataReader.GetDouble),
		TokenType.TYPE_DECIMAL => nameof(SqlDataReader.GetDecimal),
		TokenType.TYPE_STRING => nameof(SqlDataReader.GetString),
		TokenType.TYPE_DATE => $"{nameof(SqlDataReader.GetFieldValue)}<System.DateOnly>",
		TokenType.TYPE_TIME => $"{nameof(SqlDataReader.GetFieldValue)}<System.TimeOnly>",
		TokenType.TYPE_DATETIME => nameof(SqlDataReader.GetDateTime),
		TokenType.TYPE_GUID => nameof(SqlDataReader.GetGuid),
		TokenType.TYPE_BINARY or TokenType.TYPE_VARBINARY => $"{nameof(SqlDataReader.GetFieldValue)}<byte[]>",
		_ => throw new Exception($"Invalid database type `{Type}`")
	};

	/// <summary>
	/// Returns a <c>System.Data.SqlDbType.*</c> expression (optionally with size) for this token's SQL type.
	/// </summary>
	/// <param name="size">The size string to embed (e.g. <c>"50"</c> or <c>"max"</c>), or <c>null</c>.</param>
	/// <param name="allowNullSize">When <c>true</c>, a <c>string</c> type without a size emits <c>VarChar</c> instead of throwing.</param>
	public string SqlDbType(string? size = default, bool allowNullSize = false) => "System.Data.SqlDbType." + Type switch
	{
		TokenType.TYPE_BOOLEAN => nameof(System.Data.SqlDbType.Bit),
		TokenType.TYPE_INT => "BigInt",
		TokenType.TYPE_FLOAT or TokenType.TYPE_DOUBLE => nameof(System.Data.SqlDbType.Float),
		TokenType.TYPE_DECIMAL => "Decimal",
		TokenType.TYPE_STRING when size?.Equals("max", StringComparison.OrdinalIgnoreCase) == true => $"VarChar, -1",
		TokenType.TYPE_STRING when size is not null => $"VarChar, {size}",
		TokenType.TYPE_STRING when allowNullSize => $"VarChar",
		TokenType.TYPE_STRING => throw new DiagnosticException("Size cannot be null."),
		TokenType.TYPE_DATE => "Date",
		TokenType.TYPE_TIME => "Time",
		TokenType.TYPE_DATETIME => "DateTime",
		TokenType.TYPE_GUID => "UniqueIdentifier",
		TokenType.TYPE_BINARY => nameof(System.Data.SqlDbType.Binary),
		TokenType.TYPE_VARBINARY => nameof(System.Data.SqlDbType.VarBinary),
		_ => throw new Exception($"Unsupported database type `{Type}`")
	};

	/// <summary>Returns the C# type keyword or full type name for this SQL type token.</summary>
	/// <param name="tableType">Optional callback that supplies the record type name for object/table tokens.</param>
	public string CSharpType(Func<string>? tableType = default) => Type switch
	{
		TokenType.TYPE_BOOLEAN => "bool",
		TokenType.TYPE_INT => "int",
		TokenType.TYPE_FLOAT or TokenType.TYPE_DOUBLE => "double",
		TokenType.TYPE_DECIMAL => "decimal",
		TokenType.TYPE_STRING => "string",
		TokenType.TYPE_DATE => "System.DateOnly",
		TokenType.TYPE_TIME => "System.TimeOnly",
		TokenType.TYPE_DATETIME => "System.DateTime",
		TokenType.TYPE_GUID => "System.Guid",
		TokenType.TYPE_BINARY or TokenType.TYPE_VARBINARY => "byte[]",
		TokenType.TYPE_OBJECT when tableType is not null => tableType(),
		TokenType.TYPE_TABLE when tableType is not null => $"System.Collections.Generic.List<{tableType()}>",
		_ => throw new Exception($"Invalid database type `{Type}`")
	};

	/// <summary>
	/// Returns the C# literal or expression to use as the property's default value, or <c>null</c>
	/// if no default should be emitted.
	/// </summary>
	/// <param name="defaultValue">The raw default-value string from the SQL DECLARE statement.</param>
	/// <param name="tableType">Optional callback for object/table column types.</param>
	public string? CSharpValue(string? defaultValue, Func<string>? tableType = default) => Type switch
	{
		TokenType.TYPE_BOOLEAN => int.TryParse(defaultValue, out var value) && value == 0 ? null : "true",
		TokenType.TYPE_INT => defaultValue,
		TokenType.TYPE_FLOAT or TokenType.TYPE_DOUBLE => defaultValue,
		TokenType.TYPE_DECIMAL => defaultValue,
		TokenType.TYPE_STRING => defaultValue is null ? null : $"\"{defaultValue}\"",
		TokenType.TYPE_DATE => DateTime.TryParse(defaultValue, out var date) ? $"\"{date:yyyy-MM-dd}\"" : defaultValue,
		TokenType.TYPE_TIME => DateTime.TryParse(defaultValue, out var time) ? $"\"{time:HH:mm:ss.fffffff}\"" : defaultValue,
		TokenType.TYPE_DATETIME => DateTime.TryParse(defaultValue, out var date) ? $"\"{date:yyyy-MM-dd} {date:HH:mm:ss.fffffff}\"" : defaultValue,
		TokenType.TYPE_GUID => Guid.TryParse(defaultValue, out var identifier) ? $"\"{identifier}\"" : defaultValue,
		TokenType.TYPE_OBJECT when tableType is not null => tableType(),
		TokenType.TYPE_TABLE when tableType is not null => tableType(),
		_ => throw new Exception($"Invalid database type `{Type}`")
	};

	/// <summary>
	/// Generates an inline C# expression for embedding a property value into a parameterized SQL
	/// string, handling type-specific formatting (dates, strings with length guards, etc.).
	/// </summary>
	/// <param name="classname">The enclosing class name, used in overflow error messages.</param>
	/// <param name="model">The model name, used in overflow error messages.</param>
	/// <param name="index">The zero-based position of this property in the table row, for error context.</param>
	/// <param name="name">The property name, for error messages.</param>
	/// <param name="property">The C# expression that accesses the property value.</param>
	public string SqlProperty(string classname, string model, int index, string name, string property)
	{
		return Type switch
		{
			TokenType.TYPE_BOOLEAN => $"{property} ? '1' : '0'",
			TokenType.TYPE_INT => property,
			TokenType.TYPE_FLOAT or TokenType.TYPE_DOUBLE => property,
			TokenType.TYPE_DECIMAL => property,
			TokenType.TYPE_STRING => S(),
			TokenType.TYPE_DATE => D($"{property}:yyyy-MM-dd"),
			TokenType.TYPE_TIME => D($"{property}:HH:mm:ss.FFFFFFF"),
			TokenType.TYPE_DATETIME => D($"{property}:yyyy-MM-dd HH:mm:ss.FFFFFFF"),
			TokenType.TYPE_GUID => $"{property}",
			_ => throw new DiagnosticException($"Invalid model {property} type `{Type}`")
		};

		string D(string property) => $"$\"'{{{property}}}'\"";

		string S() => $""""
			{property} is null || {property}.Length <= {Value}
				? ({property} is null ? "Null" : $"'{"{"}{property}{"}"}'")
				: throw new Exception($"""
					{classname}{model}Request model table property [{model}]
					at index [{index}] has a string property [{name}]
					with more than {Value} characters.
					""")
			"""";
	}
}
