using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SQuiL.SourceGenerator.Parser;

using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace SQuiL.Models;

public class SQuiLTable(
	string NameSpace,
	string Modifiers,
	string Type,
	CodeBlock Block,
	SQuiLTableMap TableMap,
	ImmutableDictionary<string, Generator.SQuiLPartialModel> Records)
	: SQuiLProperty(Type, Block, TableMap)
{
	protected CodeBlock Block { get; } = Block;
	public SQuiLTableMap TableMap { get; } = TableMap;

	public SQuiLTable Clone()
		=> new(NameSpace, Modifiers, Type, Block with
		{
			DatabaseType = Block.DatabaseType with { }
		}, TableMap, Records);

	public bool HasParameterizedConstructor { get; init; }

	public List<string> ConstructorParameters { get; set; } = default!;

	public virtual (string TableName, ExceptionOrValue<string> Exception) GenerateCode(List<CodeItem> properties)
	{
		List<Exception> exceptions = [];

		StringWriter text = new();
		IndentedTextWriter record = new(text, "\t");

		var tableName = TableName();
		var @namespace = NameSpace;

		if (tableName.StartsWith(SourceGeneratorHelper.NamespaceName))
			@namespace = SourceGeneratorHelper.NamespaceName;

		record.Write($$"""
			{{SourceGeneratorHelper.FileHeader}}
			namespace {{@namespace}};
			
			{{Modifiers}} 
			""");

		if (Records.TryGetValue(tableName, out var partial) && partial.Syntax.ParameterList?.Parameters.Count == 0)
		{
			record.Block(tableName, () =>
			{
				var fields = partial.Syntax.Members
					.Where(p => p is PropertyDeclarationSyntax)
					.Select(p => (PropertyDeclarationSyntax)p)
					.Where(p => p is not null)
					.Select(p => p.Identifier.Text)
					.ToList() ?? [];

				var baseline = partial.Syntax.BaseList?.Types
					.SelectMany(p => Records.TryGetValue(p.Type.ToString(), out var identifier)
						? identifier.Syntax.Members
							.Where(p => p is PropertyDeclarationSyntax)
							.Select(p => (PropertyDeclarationSyntax)p)
						: null)
					.Where(p => p is not null)
					.Select(p => p.Identifier.Text)
					.ToList() ?? [];

				foreach (var item in properties)
				{
					var property = baseline.FirstOrDefault(item.Identifier.Value.Equals)
						?? fields.FirstOrDefault(item.Identifier.Value.Equals);
					if (property is not null) continue;

					var type = TableMap.TryGetName(item.Type.Value, out var tableName)
						? item.CSharpType(() => tableName)
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
			return (tableName, new AggregateException(exceptions));

		return (tableName, new ExceptionOrValue<string>(text.ToString()));

		string CamelCase(string variable) => $"{variable[0..1].ToLower()}{variable[1..]}";

		void WriteParameterizedConstructor(Func<string, string> callback)
		{
			record.Write($"{tableName}(");
			record.Indent++;
			var comma = "";
			foreach (var item in properties)
			{
				record.WriteLine(comma);
				record.Write($"{item.CSharpType()} {callback(item.Identifier.Value)}");
				comma = ",";
			}
			record.Write(")");
		}
	}
}
