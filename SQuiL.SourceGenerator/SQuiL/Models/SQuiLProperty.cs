using Microsoft.CodeAnalysis;

using SQuiL.Generator;
using SQuiL.SourceGenerator.Parser;

using System.CodeDom.Compiler;

namespace SQuiL.Models;

/// <summary>
/// Base class for a single SQL variable (scalar, table, or object) parsed from a
/// <c>DECLARE</c> statement.  Drives property-level C# code generation for request and
/// response model records.
/// </summary>
/// <param name="Block">The parsed SQL code block representing this variable.</param>
/// <param name="TableMap">The shared table-name-to-C#-type mapping used to resolve type names.</param>
public class SQuiLProperty(
	CodeBlock Block,
	SQuiLTableMap TableMap)
{
	/// <summary>
	/// Protected constructor that also sets the C# type suffix used when building the record name
	/// (e.g. <c>"Table"</c> or <c>"Object"</c>).
	/// </summary>
	protected SQuiLProperty(
		string type, CodeBlock Block, SQuiLTableMap TableMap)
		: this(Block, TableMap)
	{
		Type = type;
	}

	/// <summary>C# type suffix appended to the property name when constructing a record type name.</summary>
	public string Type { get; } = "";

	/// <summary>The C# namespace the row record is emitted into (e.g. <c>MyApp.Data.Models</c>),
	/// or empty when the record is top-level in the context namespace.</summary>
	public string RecordNamespace { get; init; } = "";

	/// <summary>
	/// Resolves the C# record type name for this property: uses the <see cref="SQuiLTableMap"/>
	/// mapping if present; otherwise combines <see cref="OriginalName"/> with <see cref="Type"/>.
	/// </summary>
	public string TableName()
	{
		if (!TableMap.TryGetName(OriginalName, out var tableName))
			tableName = OriginalName;   // was: $"{OriginalName}{Type}"

		return tableName;
	}

	/// <summary>The SQL variable base name (without the <c>@Param_</c> / <c>@Return_</c> prefix).</summary>
	public string OriginalName => Block.Name;

	/// <summary>The columns or sub-properties declared inside a table or object variable.</summary>
	public List<CodeItem> CodeItems => Block.Properties;

	/// <summary>
	/// Writes a single C# property declaration for this SQL variable into <paramref name="writer"/>,
	/// including an appropriate default initializer for tables, objects, and scalars.
	/// </summary>
	public void GenerateCode(IndentedTextWriter writer)
	{
		try
		{
			var nullable = (Block.IsTable || Block.IsObject || Block.IsNullable) ? "?" : "";

			var reference = (Block.IsTable || Block.IsObject) && RecordNamespace.Length > 0 && !TableMap.TryGetName(OriginalName, out _)
				? $"{RecordNamespace}.{TableName()}"
				: TableName();

			writer.Write($$"""public {{Block.CSharpType(reference)}}{{nullable}} {{Block.Name}} { get; set; }""");

			if (Block.IsBinary) return;
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

			// Output lists are null when their result set is absent (initialized in the reader);
			// input lists (@Params_) keep their empty-list default.
			if ((Block.CodeType & CodeType.OUTPUT) != CodeType.OUTPUT)
				writer.Write($" = [];");
			return true;
		}
	}
}
