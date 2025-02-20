using Microsoft.CodeAnalysis;

using SQuiL.Generator;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using System.CodeDom.Compiler;

namespace SQuiL.Models;

public class SQuiLProperty(
	CodeBlock Block,
	SQuiLTableMap TableMap)
{
	protected SQuiLProperty(
		string type, CodeBlock Block, SQuiLTableMap TableMap)
		: this(Block, TableMap)
	{
		Type = type;
	}

	public string Type { get; } = "";

	public string TableName()
	{
		if (SQuiLGenerator.IsError(OriginalName))
			return $"SQuiL{OriginalName}";

		if (!TableMap.TryGetName(OriginalName, out var tableName))
			tableName = $"{OriginalName}{Type}";

		return tableName;
	}

	public string OriginalName => Block.Name;

	public List<CodeItem> CodeItems => Block.Properties;

	public void GenerateCode(IndentedTextWriter writer)
	{
		try
		{
			var nullable = Block.IsNullable ? "?" : "";

			writer.Write($$"""public {{Block.CSharpType(TableName())}}{{nullable}} {{Block.Name}} { get; set; }""");

			if (Block.IsBinary) return;
			if (GenerateTable()) return;
			if (GenerateObject()) return;

			var value = Block.CSharpValue();

			if (value is not null)
				writer.Write($" = {value};");
		}
		finally
		{
			writer.WriteLine();
		}

		bool GenerateObject()
		{
			if (!Block.IsObject) return false;

			writer.Write($" = default!;");
			return true;
		}

		bool GenerateTable()
		{
			if (!Block.IsTable) return false;

			writer.Write($" = [];");
			return true;
		}
	}
}
