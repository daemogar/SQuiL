using System.Collections.Generic;
using System.Linq;

namespace SQuiL.Dialects;

/// <summary>
/// The PostgreSQL dialect (via Npgsql): the source of every PostgreSQL-specific string the generator
/// bakes into emitted C# and SQL. A temp-table-header dialect (near-twin of SQLite). Type-map,
/// temp-table, and shred members are filled in Tasks 4-6; they throw until then so the registry
/// compiles from first registration.
/// </summary>
public class PostgresDialect : ITempTableHeaderDialect
{
	public IEnumerable<string> UsingDirectives() => ["using Npgsql;", "using NpgsqlTypes;"];
	public string ProviderExceptionType() => "NpgsqlException";
	public string RuntimeBaseType() => "PostgresDataContext";
	public string DatabaseDirective(string catalog) => ""; // no USE; database fixed by the connection string
	public string VarCharType() => "NpgsqlTypes.NpgsqlDbType.Varchar";
	public string BitType() => "NpgsqlTypes.NpgsqlDbType.Boolean";

	public string TableVariableDeclaration(SQuiL.SourceGenerator.Parser.CodeBlock block, string newLine)
	{
		var name = block.TempTableName ?? block.Name;
		var cols = string.Join($",{newLine}\t", block.Properties.Select(p
			=> $"{p.Identifier.Value} {p.Type.Original}{(p.IsNullable ? "" : " Not Null")}{(p.IsPrimaryKey ? " Primary Key" : "")}"));

		return $"""
			Drop Table If Exists {name};
			Create Temp Table {name} (
				{cols});
			""".Replace("\r\n", "\n");
	}

	public string ScalarVariableDeclaration(SQuiL.SourceGenerator.Parser.CodeBlock block, string newLine)
	{
		var name = block.TempScalarTableName ?? block.Name;
		var col = block.TempScalarColumn;
		var colDef = col is not null
			? $"{col.Identifier.Value} {col.Type.Original}{(col.IsNullable ? "" : " Not Null")}{(col.IsPrimaryKey ? " Primary Key" : "")}"
			: $"{block.Name} {block.DatabaseType.Original}";

		return $"""
			Drop Table If Exists {name};
			Create Temp Table {name} (
				{colDef});
			""".Replace("\r\n", "\n");
	}

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a table/object column. Npgsql's
	/// <c>NpgsqlDataReader</c> is a full ADO.NET reader (unlike Microsoft.Data.Sqlite's), so this
	/// delegates to the shared T-SQL accessor — every method name <c>Token.DataReader()</c> emits
	/// (<c>GetInt32</c>, <c>GetGuid</c>, <c>GetFieldValue&lt;System.DateTimeOffset&gt;</c>, etc.)
	/// is valid on it.</summary>
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem item) => item.DataReader();

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a scalar block. See
	/// <see cref="ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem)"/>.</summary>
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeBlock block) => block.DataReader();

	/// <summary>
	/// The <c>NpgsqlTypes.NpgsqlDbType.*</c> parameter-type expression for a block. Unlike SQLite
	/// (four bind types, delegates to its own small map) this needs a PostgreSQL-specific map —
	/// <c>Token.SqlDbType()</c> is SQL-Server-shaped (<c>System.Data.SqlDbType.*</c>) and does not
	/// apply here.
	/// </summary>
	public string ParamTypeExpr(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> "NpgsqlTypes.NpgsqlDbType." + (block.DatabaseType.Type switch
		{
			SQuiL.Tokenizer.TokenType.TYPE_INT => "Integer",
			SQuiL.Tokenizer.TokenType.TYPE_BIGINT => "Bigint",
			SQuiL.Tokenizer.TokenType.TYPE_SMALLINT => "Smallint",
			SQuiL.Tokenizer.TokenType.TYPE_STRING => "Text",
			SQuiL.Tokenizer.TokenType.TYPE_VARBINARY or SQuiL.Tokenizer.TokenType.TYPE_BINARY
				or SQuiL.Tokenizer.TokenType.TYPE_IMAGE => "Bytea",
			SQuiL.Tokenizer.TokenType.TYPE_GUID => "Uuid",
			SQuiL.Tokenizer.TokenType.TYPE_BOOLEAN => "Boolean",
			SQuiL.Tokenizer.TokenType.TYPE_DATE => "Date",
			SQuiL.Tokenizer.TokenType.TYPE_TIME => "Time",
			SQuiL.Tokenizer.TokenType.TYPE_DATETIME => "Timestamp",
			SQuiL.Tokenizer.TokenType.TYPE_DATETIMEOFFSET => "TimestampTz",
			SQuiL.Tokenizer.TokenType.TYPE_DECIMAL or SQuiL.Tokenizer.TokenType.TYPE_MONEY => "Numeric",
			SQuiL.Tokenizer.TokenType.TYPE_FLOAT => "Real",
			SQuiL.Tokenizer.TokenType.TYPE_DOUBLE => "Double",
			_ => "Text",
		});

	public string ShredParamName(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new System.NotImplementedException();
	public string ShredStatement(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new System.NotImplementedException();
}
