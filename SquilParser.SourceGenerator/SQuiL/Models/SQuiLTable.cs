using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SquilParser.SourceGenerator.Parser;

using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace SQuiL.Models;

public class SQuiLTable(
	string NameSpace,
	string Modifiers,
	string Type,
	CodeBlock Block,
	ImmutableDictionary<string, SQuiLTableMap> TableMap,
	ImmutableDictionary<string, Generator.SQuiLPartialModel> Records)
	: SQuiLProperty(Type, Block)
{
	protected CodeBlock Block { get; } = Block;

	public bool HasParameterizedConstructor { get; init; }

	public List<string> ConstructorParameters { get; set; } = default!;

	public virtual ExceptionOrValue<string> GenerateCode()
	{
		List<Exception> exceptions = [];

		StringWriter text = new();
		IndentedTextWriter record = new(text, "\t");

		record.Write($$"""
			{{SourceGeneratorHelper.FileHeader}}
			namespace {{NameSpace}};
			
			{{Modifiers}} 
			""");

		if (Records.TryGetValue(ModelName, out var partial) && partial.Syntax.ParameterList?.Parameters.Count == 0)
		{
			record.Block(ModelName, () =>
			{
				var b = partial.Syntax.BaseList?.Types
					.SelectMany(p => Records.TryGetValue(p.Type.ToString(), out var identifier)
						? identifier.Syntax.Members
							.Where(p => p is PropertyDeclarationSyntax)
							.Select(p => (PropertyDeclarationSyntax)p)
						: null)
					.Where(p => p is not null)
					.Select(p => p.Identifier.Text)
					.ToList() ?? [];

				foreach (var item in Block.Properties)
				{
					var constructorParameter = b.FirstOrDefault(item.Identifier.Value.Equals);
					if (constructorParameter is not null) continue;

					var type = TableMap.TryGetValue(item.Type.Value, out var map)
					? item.CSharpType(() => map.TableName)
					: item.CSharpType();

					record.WriteLine($$"""public {{type}} {{item.Identifier.Value}} { get; init; }""");
					record.WriteLine();
				}

				record.Write("public ");
				WriteParameterizedConstructor(CamelCase);
				record.Block(" : this()", () =>
				{
					foreach (var item in Block.Properties)
					{
						var variable = item.Identifier.Value;
						record.WriteLine($"{variable} = {CamelCase(variable)};");
					}
				});
			});
		}
		else
		{
			WriteParameterizedConstructor(p => p);
			record.WriteLine(";");
		}

		if (exceptions.Count > 0)
			return new AggregateException(exceptions);

		return new ExceptionOrValue<string>(text.ToString());

		string CamelCase(string variable) => $"{variable[0..1].ToLower()}{variable[1..]}";

		void WriteParameterizedConstructor(Func<string, string> callback)
		{
			record.Write($"{ModelName}(");
			record.Indent++;
			var comma = "";
			foreach (var item in Block.Properties)
			{
				record.WriteLine(comma);
				record.Write($"{item.CSharpType()} {callback(item.Identifier.Value)}");
				comma = ",";
			}
			record.Write(")");
		}
	}
}
