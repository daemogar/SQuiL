using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SQuiL.Generator;

using SQuiL.SourceGenerator.Parser;

using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Diagnostics;

namespace SQuiL.Models;

/// <summary>
/// Represents one half of a query's strongly-typed C# interface: either the request model
/// (built from <c>@Param_*</c> blocks) or the response model (built from <c>@Return_*</c>
/// blocks).  Drives generation of the corresponding <c>record</c> source file.
/// </summary>
/// <param name="NameSpace">The C# namespace the model record will be emitted into.</param>
/// <param name="ModelName">The base query name; combined with <paramref name="ModelType"/> to form the full type name.</param>
/// <param name="ModelType">Either <c>"Request"</c> or <c>"Response"</c>.</param>
/// <param name="TableMap">The shared table-name-to-C#-type mapping for resolving table properties.</param>
/// <param name="Records">All partial record declarations visible in the compilation.</param>
public class SQuiLModel(
	string NameSpace,
	string ModelName,
	string ModelType,
	SQuiLTableMap TableMap,
	ImmutableDictionary<string, SQuiLPartialModel> Records)
{
	/// <summary>Full model type name, e.g. <c>MyQueryRequest</c> or <c>MyQueryResponse</c>.</summary>
	public string ModelName { get; } = $"{ModelName}{ModelType}";

	/// <summary>All properties (scalar, table, and object) belonging to this model.</summary>
	public List<SQuiLProperty> Properties { get; } = [];

	/// <summary>
	/// Builds the request and response models for a query from its parsed <paramref name="blocks"/>.
	/// </summary>
	/// <param name="namespace">The C# namespace the models will be emitted into.</param>
	/// <param name="modelname">Base name of the query (used to form the model type names).</param>
	/// <param name="blocks">All parsed code blocks from the SQL file.</param>
	/// <param name="tableMap">Shared table-name mapping for resolving cross-query types.</param>
	/// <param name="records">All partial record declarations visible in the compilation.</param>
	/// <returns>The request model and the response model as a tuple.</returns>
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

	/// <summary>
	/// Emits the complete C# source for this model record, ordering properties so scalars
	/// appear before objects and objects before tables.
	/// </summary>
	/// <returns>The generated source text, or an exception if generation failed.</returns>
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
			writer.WriteLine("public bool DebugOnly { get; set; }");
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
			if (SQuiLGenerator.IsSpecial(block.Name)
				|| InheritsProperty(ModelName, block.Name))
				CreateSpecial(block);
			else if (block.IsTable || block.IsObject)
				CreateTableObject(block);
			else
				Properties.Add(new(block, TableMap));
		}

		return this;
	}

	private void CreateSpecial(CodeBlock block)
	{
		if (!SQuiLGenerator.IsError(block.Name)) return;
		CreateTableObject(block, false);
	}

	private void CreateTableObject(CodeBlock block, bool addProperty = true)
	{
		var type = (addProperty
				? (block.IsTable ? typeof(SQuiLTable) : typeof(SQuiLObject))
				: typeof(SQuiLProperty)).Name[5..];

		if (block.Properties is null)
			throw new DiagnosticException(
				$"Cannot generate {type} `{block.Name}` for {ModelName}");

		var hasParameterizedConstructor = !Records.TryGetValue(type, out var partial)
			|| partial.Syntax.ParameterList?.Parameters.Count == 0;

		var name = $"{block.Name}{type}";

		if (addProperty) name = $"{NameSpace}{block.Name}"; else type = "";

		SQuiLTable table = block.IsTable
			? new SQuiLTable(NameSpace, Modifier(name), type, block, TableMap, Records)
			{
				HasParameterizedConstructor = hasParameterizedConstructor
			}
			: new SQuiLObject(NameSpace, Modifier(name), type, block, TableMap, Records)
			{
				HasParameterizedConstructor = hasParameterizedConstructor
			};

		if (addProperty) Properties.Add(table);
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

		if (!Records.TryGetValue(name, out var access)
			&& !Records.TryGetValue($"{SourceGeneratorHelper.NamespaceName}{name}", out access))
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
