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
		TokenType.TYPE_BIGINT => nameof(SqlDataReader.GetInt64),
		TokenType.TYPE_SMALLINT => nameof(SqlDataReader.GetInt16),
		TokenType.TYPE_TINYINT => nameof(SqlDataReader.GetByte),
		TokenType.TYPE_FLOAT => nameof(SqlDataReader.GetFloat),
		TokenType.TYPE_DOUBLE => nameof(SqlDataReader.GetDouble),
		TokenType.TYPE_DECIMAL => nameof(SqlDataReader.GetDecimal),
		TokenType.TYPE_MONEY or TokenType.TYPE_SMALLMONEY => nameof(SqlDataReader.GetDecimal),
		TokenType.TYPE_STRING => nameof(SqlDataReader.GetString),
		TokenType.TYPE_DATE => $"{nameof(SqlDataReader.GetFieldValue)}<System.DateOnly>",
		TokenType.TYPE_TIME => $"{nameof(SqlDataReader.GetFieldValue)}<System.TimeOnly>",
		TokenType.TYPE_DATETIME => nameof(SqlDataReader.GetDateTime),
		TokenType.TYPE_SMALLDATETIME => nameof(SqlDataReader.GetDateTime),
		TokenType.TYPE_DATETIMEOFFSET => $"{nameof(SqlDataReader.GetFieldValue)}<System.DateTimeOffset>",
		TokenType.TYPE_GUID => nameof(SqlDataReader.GetGuid),
		TokenType.TYPE_BINARY or TokenType.TYPE_VARBINARY or TokenType.TYPE_IMAGE or TokenType.TYPE_TIMESTAMP => $"{nameof(SqlDataReader.GetFieldValue)}<byte[]>",
		TokenType.TYPE_XML => nameof(SqlDataReader.GetString),
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
		TokenType.TYPE_INT => nameof(System.Data.SqlDbType.Int),
		TokenType.TYPE_BIGINT => nameof(System.Data.SqlDbType.BigInt),
		TokenType.TYPE_SMALLINT => nameof(System.Data.SqlDbType.SmallInt),
		TokenType.TYPE_TINYINT => nameof(System.Data.SqlDbType.TinyInt),
		TokenType.TYPE_FLOAT => nameof(System.Data.SqlDbType.Real),
		TokenType.TYPE_DOUBLE => nameof(System.Data.SqlDbType.Float),
		TokenType.TYPE_DECIMAL => "Decimal",
		TokenType.TYPE_MONEY => nameof(System.Data.SqlDbType.Money),
		TokenType.TYPE_SMALLMONEY => nameof(System.Data.SqlDbType.SmallMoney),
		TokenType.TYPE_STRING when size?.Equals("max", StringComparison.OrdinalIgnoreCase) == true => $"VarChar, -1",
		TokenType.TYPE_STRING when size is not null => $"VarChar, {size}",
		TokenType.TYPE_STRING when allowNullSize => $"VarChar",
		TokenType.TYPE_STRING => throw new DiagnosticException("Size cannot be null."),
		TokenType.TYPE_DATE => "Date",
		TokenType.TYPE_TIME => "Time",
		TokenType.TYPE_DATETIME => "DateTime",
		TokenType.TYPE_SMALLDATETIME => nameof(System.Data.SqlDbType.SmallDateTime),
		TokenType.TYPE_DATETIMEOFFSET => nameof(System.Data.SqlDbType.DateTimeOffset),
		TokenType.TYPE_GUID => "UniqueIdentifier",
		TokenType.TYPE_BINARY => nameof(System.Data.SqlDbType.Binary),
		TokenType.TYPE_VARBINARY => nameof(System.Data.SqlDbType.VarBinary),
		TokenType.TYPE_IMAGE => nameof(System.Data.SqlDbType.Image),
		TokenType.TYPE_TIMESTAMP => nameof(System.Data.SqlDbType.Timestamp),
		TokenType.TYPE_XML => nameof(System.Data.SqlDbType.Xml),
		_ => throw new Exception($"Unsupported database type `{Type}`")
	};

	/// <summary>Returns the C# type keyword or full type name for this SQL type token.</summary>
	/// <param name="tableType">Optional callback that supplies the record type name for object/table tokens.</param>
	public string CSharpType(Func<string>? tableType = default) => Type switch
	{
		TokenType.TYPE_BOOLEAN => "bool",
		TokenType.TYPE_INT => "int",
		TokenType.TYPE_BIGINT => "long",
		TokenType.TYPE_SMALLINT => "short",
		TokenType.TYPE_TINYINT => "byte",
		TokenType.TYPE_FLOAT => "float",
		TokenType.TYPE_DOUBLE => "double",
		TokenType.TYPE_DECIMAL => "decimal",
		TokenType.TYPE_MONEY or TokenType.TYPE_SMALLMONEY => "decimal",
		TokenType.TYPE_STRING => "string",
		TokenType.TYPE_DATE => "System.DateOnly",
		TokenType.TYPE_TIME => "System.TimeOnly",
		TokenType.TYPE_DATETIME => "System.DateTime",
		TokenType.TYPE_SMALLDATETIME => "System.DateTime",
		TokenType.TYPE_DATETIMEOFFSET => "System.DateTimeOffset",
		TokenType.TYPE_GUID => "System.Guid",
		TokenType.TYPE_BINARY or TokenType.TYPE_VARBINARY or TokenType.TYPE_IMAGE or TokenType.TYPE_TIMESTAMP => "byte[]",
		TokenType.TYPE_XML => "string",
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
		TokenType.TYPE_BIGINT or TokenType.TYPE_SMALLINT or TokenType.TYPE_TINYINT => defaultValue,
		TokenType.TYPE_FLOAT => defaultValue is null ? null : $"{defaultValue}f",
		TokenType.TYPE_DOUBLE => defaultValue,
		TokenType.TYPE_DECIMAL => defaultValue is null ? null : $"{defaultValue}m",
		TokenType.TYPE_MONEY or TokenType.TYPE_SMALLMONEY => defaultValue is null ? null : $"{defaultValue}m",
		TokenType.TYPE_STRING => defaultValue is null ? null : $"\"{defaultValue}\"",
		TokenType.TYPE_DATE => DateTime.TryParse(defaultValue, out var date) ? $"System.DateOnly.Parse(\"{date:yyyy-MM-dd}\", System.Globalization.CultureInfo.InvariantCulture)" : defaultValue,
		TokenType.TYPE_TIME => DateTime.TryParse(defaultValue, out var time) ? $"System.TimeOnly.Parse(\"{time:HH:mm:ss.fffffff}\", System.Globalization.CultureInfo.InvariantCulture)" : defaultValue,
		TokenType.TYPE_DATETIME => DateTime.TryParse(defaultValue, out var date) ? $"System.DateTime.Parse(\"{date:yyyy-MM-dd} {date:HH:mm:ss.fffffff}\", System.Globalization.CultureInfo.InvariantCulture)" : defaultValue,
		TokenType.TYPE_SMALLDATETIME => DateTime.TryParse(defaultValue, out var smallDate) ? $"System.DateTime.Parse(\"{smallDate:yyyy-MM-dd} {smallDate:HH:mm:ss.fffffff}\", System.Globalization.CultureInfo.InvariantCulture)" : defaultValue,
		TokenType.TYPE_DATETIMEOFFSET => DateTimeOffset.TryParse(defaultValue, out var dto) ? $"System.DateTimeOffset.Parse(\"{dto:yyyy-MM-dd HH:mm:ss.fffffff zzz}\", System.Globalization.CultureInfo.InvariantCulture)" : defaultValue,
		TokenType.TYPE_GUID => Guid.TryParse(defaultValue, out var identifier) ? $"System.Guid.Parse(\"{identifier}\")" : defaultValue,
		TokenType.TYPE_XML => defaultValue is null ? null : $"\"{defaultValue}\"",
		TokenType.TYPE_OBJECT when tableType is not null => tableType(),
		TokenType.TYPE_TABLE when tableType is not null => tableType(),
		_ => throw new Exception($"Invalid database type `{Type}`")
	};
}
