using SquilParser.SourceGenerator.Parser;

using System.CodeDom.Compiler;

namespace SQuiL.Models;

public class SQuiLProperty(string Type, CodeBlock Block)
{
	public string ModelName { get; } = Type;

	public void GenerateCode(IndentedTextWriter writer)
	{
		try
		{
			var nullable = Block.IsNullable ? "?" : "";

			writer.Write($$"""public {{Block.CSharpType(ModelName)}}{{nullable}} {{Block.Name}} { get; set; }""");

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
