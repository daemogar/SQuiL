using SQuiL.Tokenizer;

namespace SQuiL.SourceGenerator.Parser;

public record CodeBlock(CodeType CodeType, Token DatabaseType, string Name, string? DefaultValue)
{
	public bool IsObject { get; }

	public bool IsTable { get; }

	public bool IsRequired { get; }

	public bool IsNullable => !IsTable && (IsRequired || DefaultValue is null || DefaultValue == "Null");

	public string? Size { get; set; }

	public List<CodeItem> Properties { get; } = default!;

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
		else
		{
			IsRequired = true;
		}
	}

	public string Source() => ToString();

	public string SqlDbType() => DatabaseType.SqlDbType(Size);

	public string DataReader() => DatabaseType.DataReader();

	public string CSharpType(string tablename)
	=> DatabaseType.CSharpType(() => tablename);

	public string CSharpType_Deprecated(string modelname)
		=> DatabaseType.CSharpType(() => $"{modelname}{Name}Table");

	public string? CSharpValue() => DatabaseType.CSharpValue(DefaultValue);
}
