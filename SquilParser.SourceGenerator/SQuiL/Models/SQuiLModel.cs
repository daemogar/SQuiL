using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SQuiL.Generator;

using SquilParser.SourceGenerator.Parser;

using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace SQuiL.Models;

public class SQuiLModel(
	string NameSpace,
	string ModelName,
	string ModelType,
	SQuiLTableMap TableMap,
	ImmutableDictionary<string, SQuiLPartialModel> Records)
{
	private string Scope { get; } = ModelName;

	public string ModelName { get; } = $"{ModelName}{ModelType}";

	public List<SQuiLProperty> Properties { get; } = [];

	public static (SQuiLModel Request, SQuiLModel Response) Create(
		string @namespace,
		string modelname,
		List<CodeBlock> blocks,
		SQuiLTableMap tableMap,
		ImmutableDictionary<string, SQuiLPartialModel> records)
	{
		var request = new SQuiLModel(@namespace, modelname, "Request", tableMap, records)
			.Build(blocks.Where(p => (p.CodeType & CodeType.INPUT) == CodeType.INPUT));

		var response = new SQuiLModel(@namespace, modelname, "Response", tableMap, records)
			.Build(blocks.Where(p => (p.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT));

		return (request, response);
	}

	public ExceptionOrValue<string> GenerateCode()
	{
		StringWriter text = new();
		IndentedTextWriter writer = new(text, "\t");

		writer.WriteLine($$"""
			{{SourceGeneratorHelper.FileHeader}}
			namespace {{NameSpace}};
			
			{{Modifier(ModelName)}} {{ModelName}}
			{
			""");

		writer.Indent++;

		Action newline = () => { };

		if (ModelType == "Request")
		{
			writer.WriteLine("public bool Debug { get; set; }");
			newline = writer.WriteLine;
		}

		foreach (var property in Properties.OrderBy(p => p is SQuiLObject ? 1 : p is SQuiLTable ? 2 : 0))
		{
			newline();
			property.GenerateCode(writer);
			newline = writer.WriteLine;
		}

		writer.Indent--;
		writer.WriteLine('}');

		return new ExceptionOrValue<string>(text.ToString());
	}

	private SQuiLModel Build(IEnumerable<CodeBlock> blocks)
	{
		foreach (var block in blocks)
		{
			if (block.Name == SQuiLGenerator.Debug
				|| block.Name == SQuiLGenerator.EnvironmentName
				|| InheritsProperty(ModelName, block.Name))
				continue;

			if (block.IsTable)
				Create<SQuiLTable>(block, (p, q) => new(NameSpace, Modifier(p), p, block, TableMap, Records)
				{
					HasParameterizedConstructor = q
				});
			else if (block.IsObject)
				Create<SQuiLObject>(block, (p, q) => new(NameSpace, Modifier(p), p, block, TableMap, Records)
				{
					HasParameterizedConstructor = q
				});
			else
				Properties.Add(new(ModelName, block, TableMap));
		}

		return this;
	}

	private void Create<T>(CodeBlock block, Func<string, bool, T> callback) where T : SQuiLTable
	{
		var type = typeof(T).Name[5..];

		if (block.Properties is null)
			throw new DiagnosticException(
				$"Cannot generate {type} `{block.Name}` for {ModelName}");

		type = $"{Scope}{block.Name}{type}";

		var hasParameterizedConstructor = !Records.TryGetValue(type, out var partial)
			|| partial.Syntax.ParameterList?.Parameters.Count == 0;

		var table = callback(type, hasParameterizedConstructor);
		Properties.Add(table);
		TableMap.Add(table);

		if (hasParameterizedConstructor)
			return;

		table.ConstructorParameters = partial!.Syntax.BaseList?.Types
			.SelectMany(p => Records.TryGetValue(p.Type.ToString(), out var identifier)
				? identifier.Syntax.Members
					.Where(p => p is PropertyDeclarationSyntax)
					.Select(p => (PropertyDeclarationSyntax)p)
				: null)
			.Where(p => p is not null)
			.Select(p => p.Identifier.Text)
			.ToList() ?? [];
	}

	private string Modifier(string name)
	{
		var modifiers = "partial record";

		if (!Records.TryGetValue(name, out var access))
			return $"public {modifiers}";

		if (access.Syntax.Modifiers.Any(SyntaxKind.PublicKeyword))
			return modifiers;

		var badModifiers = "";
		if (access.Syntax.Modifiers.Any(SyntaxKind.InternalKeyword))
			badModifiers += ",internal";
		if (access.Syntax.Modifiers.Any(SyntaxKind.PrivateKeyword))
			badModifiers += ",private";
		if (access.Syntax.Modifiers.Any(SyntaxKind.ProtectedKeyword))
			badModifiers += ",protected";

		if (badModifiers.Length == 0)
			return $"public {modifiers}";

		throw new DiagnosticException(
		$"{name} cannot use keyword modifiers [{badModifiers[1..]}], " +
		$"either use public or exlcude the use of a keyword modifier.");
	}

	private bool InheritsProperty(string model, string name)
	{
		if (!Records.TryGetValue(model, out var record))
			return false;

		foreach (var member in record.Syntax.Members)
		{
			if (member is not PropertyDeclarationSyntax property)
				continue;

			if (property.Identifier.Value?.Equals(name) == true)
				return true;
		}

		if (record.Syntax.ParameterList is not null)
			foreach (var member in record.Syntax.ParameterList.Parameters)
			{
				if (member.Identifier.Value?.Equals(name) == true)
					return true;
			}

		if (record.Syntax.BaseList is not null)
			foreach (var type in record.Syntax.BaseList.Types)
			{
				if (type switch
				{
					SimpleBaseTypeSyntax basic => InheritsProperty(basic.Type.ToString(), name),
					PrimaryConstructorBaseTypeSyntax primary => InheritsProperty(primary.Type.ToString(), name),
					_ => false
				}) return true;
			}

		return false;
	}
}
