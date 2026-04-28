using SQuiL.Tokenizer;

namespace SQuiL.SourceGenerator.Parser;

/// <summary>
/// Intermediate representation of one parsed SQL <c>DECLARE</c>, <c>USE</c>, or <c>BODY</c>
/// statement.  Produced by <see cref="SQuiLParser"/> and consumed by the model builders and
/// code generators.
/// </summary>
/// <param name="CodeType">The classified role of this block (input, output, using, body, etc.).</param>
/// <param name="DatabaseType">The SQL type token that describes the block's data type.</param>
/// <param name="Name">The variable base name (without the <c>@Param_</c> / <c>@Return_</c> prefix).</param>
/// <param name="DefaultValue">The literal default value string, if any, from the DECLARE statement.</param>
public record CodeBlock(CodeType CodeType, Token DatabaseType, string Name, string? DefaultValue)
{
	/// <summary><c>true</c> when the variable represents a single-row structured type (<c>table</c> declared as object).</summary>
	public bool IsObject { get; }

	/// <summary><c>true</c> when the variable represents a multi-row table-valued type.</summary>
	public bool IsTable { get; }

	/// <summary><c>true</c> when the variable is a binary or varbinary type.</summary>
	public bool IsBinary { get; }

	/// <summary><c>true</c> when the variable has no default value and must be supplied by the caller.</summary>
	public bool IsRequired { get; }

	/// <summary>
	/// <c>true</c> when the generated C# property should be nullable —
	/// scalars without a default, binary types, and non-table types that default to <c>Null</c>.
	/// </summary>
	public bool IsNullable
	{
		get
		{
			if (IsTable || IsBinary)
				return true;

			if (IsRequired || DefaultValue is null || DefaultValue == "Null")
				return true;

			return false;
		}
	}

	/// <summary>The string size or precision extracted from the type token (e.g. <c>"50"</c> for <c>varchar(50)</c>).</summary>
	public string? Size { get; set; }

	/// <summary>Columns or sub-properties for table and object variable types; <c>null</c> for scalars.</summary>
	public List<CodeItem> Properties { get; } = default!;

	/// <summary>
	/// Convenience constructor that derives the block name and default value from the type token directly.
	/// </summary>
	public CodeBlock(CodeType CodeType, Token Token)
	: this(CodeType, Token, Token.Value, default)
	{
		if (Token.Type == TokenType.TYPE_TABLE)
		{
			IsTable = true;
			Properties = [];
		}
		else if (Token.Type == TokenType.TYPE_OBJECT)
		{
			IsObject = true;
			Properties = [];
		}
		else if (Token.Type == TokenType.TYPE_BINARY || Token.Type == TokenType.TYPE_VARBINARY)
		{
			IsBinary = true;
			IsRequired = true;
		}
		else
		{
			IsRequired = true;
		}
	}

	/// <summary>Returns a human-readable summary of this block (delegates to record <c>ToString</c>).</summary>
	public string Source() => ToString();

	/// <summary>Returns the <c>System.Data.SqlDbType.*</c> expression for this block's type, including size.</summary>
	public string SqlDbType() => DatabaseType.SqlDbType(Size);

	/// <summary>Returns the <c>reader.GetXxx</c> method fragment appropriate for this block's SQL type.</summary>
	public string DataReader() => DatabaseType.DataReader();

	/// <summary>Returns the C# type string, using <paramref name="tablename"/> for object/table types.</summary>
	public string CSharpType(string tablename)
	=> DatabaseType.CSharpType(() => tablename);

	/// <summary>Legacy helper that derives a table type name from the model name and block name.</summary>
	public string CSharpType_Deprecated(string modelname)
		=> DatabaseType.CSharpType(() => $"{modelname}{Name}Table");

	/// <summary>Returns the C# default-value expression for this block, or <c>null</c> if none.</summary>
	public string? CSharpValue() => DatabaseType.CSharpValue(DefaultValue);
}
