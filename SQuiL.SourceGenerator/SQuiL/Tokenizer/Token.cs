using Microsoft.CodeAnalysis;
using Microsoft.Data.SqlClient;

namespace SQuiL.Tokenizer;

public record Token(TokenType Type, int Offset, string Value)
{
	public string? Original { get; init; }

	public Token(TokenType Type, int Offset) : this(Type, Offset, default!) { }

	public static Token END { get; } = new(TokenType.END, -1);

	public string Expect() => $"{Type:G} => {Value}";

	public string DataReader() => "reader." + Type switch
	{
		TokenType.TYPE_BOOLEAN => "GetBoolean",
		TokenType.TYPE_INT => "GetInt32",
		TokenType.TYPE_FLOAT => nameof(SqlDataReader.GetFloat),
		TokenType.TYPE_DOUBLE => nameof(SqlDataReader.GetDouble),
		TokenType.TYPE_DECIMAL => "GetDecimal",
		TokenType.TYPE_STRING => "GetString",
		TokenType.TYPE_DATE => "GetDateTime",
		TokenType.TYPE_TIME => "GetDateTime",
		TokenType.TYPE_DATETIME => "GetDateTime",
		TokenType.TYPE_GUID => "GetGuid",
		TokenType.TYPE_BINARY or TokenType.TYPE_VARBINARY => "GetFieldValue<byte[]>",
		_ => throw new Exception($"Invalid database type `{Type}`")
	};

	public string SqlDbType(string? size = default, bool allowNullSize = false) => "System.Data.SqlDbType." + Type switch
	{
		TokenType.TYPE_BOOLEAN => "Bit",
		TokenType.TYPE_INT => "BigInt",
		TokenType.TYPE_FLOAT or TokenType.TYPE_DOUBLE => nameof(System.Data.SqlDbType.Float),
		TokenType.TYPE_DECIMAL => "Decimal",
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
