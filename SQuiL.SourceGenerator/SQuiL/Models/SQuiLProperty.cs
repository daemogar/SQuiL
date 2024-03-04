using Microsoft.CodeAnalysis;

using SQuiL.SourceGenerator.Parser;

using System.CodeDom.Compiler;

namespace SQuiL.Models;

public class SQuiLProperty(
	string Type,
	CodeBlock Block,
	SQuiLTableMap TableMap)
{
	public string ModelName { get; } = Type;

	public string TableName()
	{
		if (!TableMap.TryGetName(OriginalName, out var tableName))
			tableName = ModelName;
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
