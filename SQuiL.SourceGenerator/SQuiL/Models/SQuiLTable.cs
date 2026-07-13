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

	/// <summary>
	/// The query method/file name that first declared this table variable, used to
	/// embed a cross-file "first declared in" hint in SP0017 messages.
	/// Set by the caller immediately after construction; empty string when unknown.
	/// </summary>
	public string SourceName { get; init; } = "";

	/// <summary>
	/// 1-based line number of this table's declaration within its source SQL file, used to
	/// point SP0017 at the first declaration so the developer can navigate to it. <c>0</c> when
	/// the line is unknown (e.g. the cross-query merge path in <see cref="SQuiLTableMap.GenerateCode"/>).
	/// </summary>
	public int SourceLine { get; init; }

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
	/// Nested-objects (Task 4/13): one entry per child edge rooted at this table/object, added as
	/// a settable, nullable member alongside the columns-only positional constructor.
	/// <c>TypeName</c> is pre-formatted by the caller (<c>SQuiLModel.CreateTableObject</c>) to
	/// already include the <c>List&lt;...&gt;</c> wrapper for a list child, or just the bare record
	/// type for an object child; <c>IsList</c> is carried for callers that need to branch on
	/// cardinality but is not required by <see cref="GenerateCode(List{CodeItem})"/> itself.
	/// <c>Initializer</c> is the trailing member initializer text (e.g. <c>" = [];"</c> for an
	/// INPUT/request list child, per the "input lists keep = []" rule) or empty for OUTPUT/response
	/// children and object children — appended verbatim after the member declaration.
	/// </summary>
	public IReadOnlyList<(string Name, string TypeName, bool IsList, string Initializer)> ChildMembers { get; set; } = [];

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
		// Only move auto-generated records into RecordNamespace; [SQuiLTable]-mapped records
		// (user-provided partials) live in the context namespace and must not be relocated.
		var isMapped = TableMap.TryGetName(OriginalName, out _);
		var @namespace = RecordNamespace.Length > 0 && !isMapped ? RecordNamespace : NameSpace;

		// SP0018 (reported in SQuiLGenerator): the user's partial owns a primary
		// constructor, so emitting our parameter list would add CS8863 noise on top
		// of the diagnostic. Emit nothing for this type.
		if (Records.TryGetValue(tableName, out var userPartial)
			&& userPartial.Syntax.ParameterList?.Parameters.Count > 0)
			return (tableName, new ExceptionOrValue<string>(string.Empty));

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

					var initializer = item.DefaultValue is null ? "" : $" = {item.CSharpValue()};";
					record.WriteLine($$"""public {{type}} {{item.Identifier.Value}} { get; init; }{{initializer}}""");
					record.WriteLine();
				}

				record.Write("public ");
				WriteParameterizedConstructor(CamelCase);
				record.Block(" : this()", () =>
				{
					foreach (var item in Block.Properties.Where(p => p.DefaultValue is null))
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
			var defaulted = properties.Where(p => p.DefaultValue is not null).ToList();
			if (ChildMembers.Count == 0 && defaulted.Count == 0)
				record.WriteLine(";");
			else
			{
				record.WriteLine();
				record.WriteLine("{");
				record.Indent++;
				foreach (var item in defaulted)
					record.WriteLine($"public {item.CSharpType()} {item.Identifier.Value} {{ get; init; }} = {item.CSharpValue()};");
				foreach (var (name, typeName, _, initializer) in ChildMembers)
					record.WriteLine($"public {typeName}? {name} {{ get; set; }}{initializer}");
				record.Indent--;
				record.WriteLine("}");
			}
		}

		if (exceptions.Count > 0)
			return (tableName, new AggregateException(exceptions));

		return (tableName, new ExceptionOrValue<string>(text.ToString()));

		string CamelCase(string variable) => $"{variable[0..1].ToLower()}{variable[1..]}";

		void WriteParameterizedConstructor(Func<string, string> callback)
		{
			// Only non-defaulted columns are positional ctor params; defaulted
			// columns are emitted by the caller as init properties. This lifts the
			// trailing-only restriction (defaults may sit in any position).
			var positional = properties.Where(p => p.DefaultValue is null).ToList();

			record.Write($"{tableName}(");
			record.Indent++;
			var comma = "";
			foreach (var item in positional)
			{
				record.WriteLine(comma);
				record.Write($"{item.CSharpType()} {callback(item.Identifier.Value)}");
				comma = ",";
			}
			record.Write(")");
			record.Indent--;
		}
	}
}
