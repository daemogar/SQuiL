using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SQuiL.SourceGenerator.Parser;

using System;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace SQuiL.Models;

/// <summary>
/// Models a SQL table-valued parameter or return table (e.g. <c>@Params_Name table(...)</c>
/// or <c>@Returns_Name table(...)</c>) and drives generation of the corresponding C# record type.
/// Inherits property-level code generation from <see cref="SQuiLProperty"/>.
/// </summary>
/// <param name="NameSpace">The C# namespace the generated record will be emitted into.</param>
/// <param name="Modifiers">The C# access and type modifiers for the record (e.g. <c>public partial record</c>).</param>
/// <param name="Type">The type-name suffix appended when building the record name (e.g. <c>"Table"</c>).</param>
/// <param name="Block">The parsed SQL code block that defines this table's columns and metadata.</param>
/// <param name="TableMap">The shared table-name-to-C#-type mapping used to resolve cross-query type names.</param>
/// <param name="Records">All partial record declarations visible in the compilation, used to merge hand-written properties.</param>
public class SQuiLTable(
	string NameSpace,
	string Modifiers,
	string Type,
	CodeBlock Block,
	SQuiLTableMap TableMap,
	ImmutableDictionary<string, Generator.SQuiLPartialModel> Records)
	: SQuiLProperty(Type, Block, TableMap)
{
	/// <summary>The parsed SQL block that defines this table's columns and metadata.</summary>
	protected CodeBlock Block { get; } = Block;

	/// <summary>The shared table-name-to-C#-type mapping used to resolve cross-query type names.</summary>
	public SQuiLTableMap TableMap { get; } = TableMap;

	/// <summary>Creates a shallow clone of this table model with a fresh database-type token copy.</summary>
	public SQuiLTable Clone()
		=> new(NameSpace, Modifiers, Type, Block with
		{
			DatabaseType = Block.DatabaseType with { }
		}, TableMap, Records);

	/// <summary>
	/// <c>true</c> when the user has NOT declared an explicit primary constructor on the partial
	/// record, so the generator must emit all constructor parameters itself.
	/// </summary>
	public bool HasParameterizedConstructor { get; init; }

	/// <summary>
	/// When the user provides their own parameterized constructor, lists the property names
	/// inherited from the base type that the generated constructor should pass through.
	/// </summary>
	public List<string> ConstructorParameters { get; set; } = default!;

	/// <summary>
	/// Generates the C# source text for this table's record type, merging <paramref name="properties"/>
	/// with any hand-written properties found on the user's partial record.
	/// </summary>
	/// <param name="properties">The complete merged column list for this table type.</param>
	/// <returns>The type name and the generated source (or an exception on failure).</returns>
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
