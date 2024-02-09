﻿using SQuiL.Tokenizer;

namespace SquilParser.SourceGenerator.Parser;

public record CodeBlock(CodeType CodeType, Token DatabaseType, string Name, string? DefaultValue)
{
	public bool IsTable { get; }

	public bool IsRequired { get; }

	public bool IsNullable => !IsTable && (IsRequired || DefaultValue is null || DefaultValue == "Null");

	public string? Size { get; set; }

	public List<CodeItem> Table { get; } = default!;

	public CodeBlock(CodeType CodeType, Token Token)
	: this(CodeType, Token, Token.Value, default)
	{
		if (Token.Type == TokenType.TYPE_TABLE)
		{
			IsTable = true;
			Table = [];
		}
		else
		{
			IsRequired = true;
		}
	}

	public string Source() => ToString();

	public string SqlDbType() => DatabaseType.SqlDbType(Size);

	public string DataReader() => DatabaseType.DataReader();

	public string CSharpType() => DatabaseType.CSharpType();
	public string CSharpType(string modelname)
		=> DatabaseType.CSharpType(() => $"{modelname}{Name}Table");

	public string? CSharpValue() => DatabaseType.CSharpValue(DefaultValue);
}
