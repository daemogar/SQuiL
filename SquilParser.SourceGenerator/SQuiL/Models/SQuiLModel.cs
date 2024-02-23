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
	string ClassName,
	string ModelName,
	string ModelType,
	CodeType CodeType,
	IEnumerable<CodeBlock> Blocks,
	ImmutableDictionary<string, SQuiLPartialModel> Records)
{
  public static (IEnumerable<Exception> Exceptions, IEnumerable<(string HintName, string TableName, string Text)> Sources) GenerateModelCode(
	  string @namespace,
	  string classname,
	  string modelname,
	  List<CodeBlock> blocks,
	  ImmutableDictionary<string, SQuiLTableMap> tableMap,
	  ImmutableDictionary<string, SQuiLPartialModel> records)
  {
	var request = new SQuiLModel(
		@namespace, classname, modelname, "Request", CodeType.INPUT, blocks, records);
	var response = new SQuiLModel(
		@namespace, classname, modelname, "Response", CodeType.OUTPUT, blocks, records);

	var (exceptions, sources) = request.GenerateCode(p =>
	{
	  if (!blocks.Any(p => p.Name == "Debug"))
		p.WriteLine("public bool Debug { get; set; }");
	});
	var (e1, s1) = response.GenerateCode(p => { });

	return (exceptions.Union(e1), sources.Union(s1)
		.Select(p => (
			$"{@namespace}.{classname}.{modelname}.{p.HintName}.g.cs",
			p.HintName,
			p.Text
		)));
  }

  private string ModelName { get; } = $"{ClassName}{ModelName}{ModelType}";

  public List<(string HintName, string Text)> Tables { get; } = [];

  public (List<Exception> Exceptions, (string HintName, string Text)[] Sources) GenerateCode(
	  Action<IndentedTextWriter> callback)
  {
	List<Exception> exceptions = [];
	var blocks = Blocks.Where(p => (p.CodeType & CodeType) == CodeType);

	StringWriter text = new();
	IndentedTextWriter writer = new(text, "\t");
	writer.WriteLine($$"""
			{{SourceGeneratorHelper.FileHeader}}
			namespace {{NameSpace}};
			
			{{Modifier(ModelName)}}partial record {{ModelName}}
			{
			""");
	writer.Indent++;
	callback(writer);
	foreach (var block in blocks.OrderBy(p => p.IsTable))
	  GeneratePropertyCode(writer, block);
	writer.Indent--;
	writer.WriteLine("}");
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

	bool InheritsProperty(string model, string name)
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

	void GeneratePropertyCode(IndentedTextWriter writer, CodeBlock block)
	{
	  if (block.Name == SQuiLGenerator.Debug || block.Name == SQuiLGenerator.EnvironmentName) return;
	  if (InheritsProperty(ModelName, block.Name)) return;

	  var nullable = block.IsNullable ? "?" : "";

	  writer.Write($$"""public {{block.CSharpType(ModelName)}}{{nullable}} {{block.Name}} { get; set; }""");

	  if (!block.IsTable)
	  {
		var value = block.CSharpValue();

		if (value is not null && block.Name != "Debug")
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
			namespace {{NameSpace}};
			
			{{Modifier(type)}}partial record 
			""");

	  if (Records.TryGetValue(type, out var partial) && partial.Syntax.ParameterList?.Parameters.Count == 0)
	  {
		
		checked type if in tablemap and replace if so and only output if usefirst

		record.Block(type, () =>
		{
		  foreach (var item in block.Table)
		  {
			var a = partial?.Syntax.BaseList?.Types
						.SelectMany(p => Records.TryGetValue(p.Type.ToString(), out var identifier)
							? identifier.Syntax.Members
								.Where(p => p is PropertyDeclarationSyntax)
								.Select(p => (PropertyDeclarationSyntax)p)
							: null)
						?.FirstOrDefault(q => q.Identifier.Text == item.Identifier.Value);

			if (a is not null)
			  continue;

			record.WriteLine($$"""{{item.CSharpType()}} {{item.Identifier.Value}} { get; init; }""");
			record.WriteLine();
		  }

		  record.Write("public ");
		  WriteParameterizedConstructor(CamelCase);
		  record.Block(" : this()", () =>
				  {
					foreach (var item in block.Table)
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

	  Tables.Add((block.Name + "Table", text.ToString()));

	  writer.WriteLine($$""" = [];""");

	  void WriteParameterizedConstructor(Func<string, string> callback)
	  {
		record.Write($"{type}(");
		record.Indent++;
		var comma = "";
		foreach (var item in block.Table)
		{
		  record.WriteLine(comma);
		  record.Write($"{item.CSharpType()} {callback(item.Identifier.Value)}");
		  comma = ",";
		}
		record.Write(")");
	  }
	}

	string CamelCase(string variable) => $"{variable[0..1].ToLower()}{variable[1..]}";
  }

  protected void GenerateArgumentCode(
	  IndentedTextWriter writer, CodeBlock block)
  {
	writer.Write($"{block.CSharpType(ModelName)} {block.Name}");
  }
}
