using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SQuiL.Generator;
using SQuiL.Tokenizer;

using SquilParser.SourceGenerator.Parser;

using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace SQuiL.Models;

public class SQuiLModel(
	string NameSpace,
	string ClassName,
	string ModelName,
	string ModelType,
	CodeType CodeType,
	IEnumerable<CodeBlock> Blocks,
	ImmutableDictionary<string, SQuiLPartialModel> Records)
{
	public static (IEnumerable<Exception> Exceptions, IEnumerable<(string HintName, string Text)> Sources) GenerateModelCode(
		string @namespace,
		string classname,
		string modelname,
		List<CodeBlock> blocks,
		ImmutableDictionary<string, SQuiLPartialModel> records)
	{
		var request = new SQuiLModel(
			@namespace, classname, modelname, "Request", CodeType.INPUT, blocks, records);
		var response = new SQuiLModel(
			@namespace, classname, modelname, "Response", CodeType.OUTPUT, blocks, records);

		var (exceptions, sources) = request.GenerateCode();
		var (e1, s1) = response.GenerateCode();

		return (exceptions.Union(e1), sources.Union(s1)
			.Select(p => p with
			{
				HintName = $"{@namespace}.{classname}.{modelname}.{p.HintName}.g.cs"
			}));
	}

	private string ModelName { get; } = $"{ClassName}{ModelName}{ModelType}";

	public List<(string HintName, string Text)> Tables { get; } = [];

	public (List<Exception> Exceptions, (string HintName, string Text)[] Sources) GenerateCode()
	{
		List<Exception> exceptions = [];
		var blocks = Blocks.Where(p => (p.CodeType & CodeType) == CodeType);

		StringWriter text = new();
		IndentedTextWriter writer = new(text, "\t");
		writer.WriteLine($$"""
			{{SourceGeneratorHelper.FileHeader}}
			namespace {{NameSpace}}
			{
				{{Modifier(ModelName)}}partial record {{ModelName}}()
				{
			""");
		writer.Indent += 2;
		foreach (var block in blocks.OrderBy(p => p.IsTable))
			GeneratePropertyCode(writer, block);
		writer.Indent -= 2;
		writer.WriteLine($$"""
			}
		}
		""");
		return (exceptions, [
			($"{ModelType}Model", text.ToString()),
			.. Tables
		]);

		string Modifier(string name)
		{
			if (!Records.TryGetValue(name, out var access))
				return "public ";

			if (access.Syntax.Modifiers.Any(SyntaxKind.PublicKeyword))
				return "";

			var modifiers = "";

			if (access.Syntax.Modifiers.Any(SyntaxKind.InternalKeyword))
				modifiers += ",internal";
			if (access.Syntax.Modifiers.Any(SyntaxKind.PrivateKeyword))
				modifiers += ",private";
			if (access.Syntax.Modifiers.Any(SyntaxKind.ProtectedKeyword))
				modifiers += ",protected";

			if (modifiers.Length == 0)
				return "public ";

			var message = $"{name} cannot use keyword modifiers [{modifiers[1..]}], " +
				$"either use public or exlcude the use of a keyword modifier.";

			exceptions.Add(new Exception(message));
			return default!;
		}

		void GeneratePropertyCode(IndentedTextWriter writer, CodeBlock block)
		{
			var nullable = "";
			if (!block.IsTable && (block.IsRequired || block.DefaultValue is null || block.DefaultValue == "Null"))
				nullable = "?";

			writer.Write($$"""public {{block.CSharpType(ClassName, ModelType)}}{{nullable}} {{block.Name}} { get; init; }""");

			if (!block.IsTable)
			{
				var value = block.CSharpValue();

				if (value is not null)
					writer.Write($$""" = {{value}};""");

				writer.WriteLine();

				return;
			}

			var type = ModelName + block.Name + "Table";

			if (block.Table is null)
				throw new DiagnosticException(
					$"Cannot generate table `{type}`");

			StringWriter text = new();
			IndentedTextWriter record = new(text, "\t");

			record.Write($$"""
			{{SourceGeneratorHelper.FileHeader}}
			namespace {{SourceGeneratorHelper.NamespaceName}};
			
			{{Modifier(type)}}partial record {{type}}(
			""");
			record.Indent++;
			var comma = "";
			foreach (var item in block.Table)
			{
				record.WriteLine(comma);
				record.Write($"{item.CSharpType()} {item.Identifier.Value}");
				comma = ",";
			}
			record.WriteLine(");");

			Tables.Add((block.Name + "Table", text.ToString()));
			writer.WriteLine($$""" = [];""");
		}
	}

	protected void GenerateArgumentCode(
		IndentedTextWriter writer, CodeBlock block)
	{
		writer.Write($"{block.CSharpType(ClassName, ModelType)} {block.Name}");
	}
}
