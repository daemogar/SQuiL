using System.Collections.Generic;
using System.Linq;

namespace SQuiL.Dialects;

/// <summary>
/// The PostgreSQL dialect (via Npgsql): the source of every PostgreSQL-specific string the generator
/// bakes into emitted C# and SQL. A temp-table-header dialect (near-twin of SQLite). Type-map
/// (Task 4), temp-table header (Task 5), and shred (Task 6) members are all implemented — no
/// member throws <see cref="System.NotImplementedException"/> any more.
/// </summary>
public class PostgresDialect : ITempTableHeaderDialect
{
	public IEnumerable<string> UsingDirectives() => ["using Npgsql;", "using NpgsqlTypes;"];
	public string ProviderExceptionType() => "NpgsqlException";
	public string RuntimeBaseType() => "PostgresDataContext";
	public string DatabaseDirective(string catalog) => ""; // no USE; database fixed by the connection string
	public string VarCharType() => "NpgsqlTypes.NpgsqlDbType.Varchar";
	public string BitType() => "NpgsqlTypes.NpgsqlDbType.Boolean";

	// Temp-table-header dialects declare scalars as single-column temp tables, so the author
	// writes `Select X From Return_X` — a real, already-named column. Nothing to rewrite.
	public string RewriteOutputSelects(string body, IEnumerable<SQuiL.SourceGenerator.Parser.CodeBlock> outputs) => body;

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

	/// <summary>
	/// Returns the JSON parameter name for the given input block:
	/// <c>@__json_Params_&lt;Name&gt;</c> for a table, <c>@__json_Param_&lt;Name&gt;</c> for an object.
	/// Identical shape to <c>SqlServerDialect.ShredParamName</c>/<c>SqliteDialect.ShredParamName</c>
	/// so the emitter's call sites stay dialect-agnostic.
	/// </summary>
	public string ShredParamName(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> $"@__json_Param{(block.IsTable ? "s" : "")}_{block.Name}";

	/// <summary>
	/// Builds the PostgreSQL <c>Insert Into … Select … From json_to_recordset(…) AS x(…);</c>
	/// shred — the PG analogue of SQL Server's <c>OpenJson … With (…)</c> shred (typed AS-column-
	/// list, unlike SQLite's untyped <c>json_each</c>). The insert target and insert column list
	/// are BARE (Option B; PG folds unquoted identifiers to lowercase, matching the bare DDL this
	/// dialect's <see cref="TableVariableDeclaration"/> emits). The <c>json_to_recordset</c>
	/// AS-column-list is the one place this dialect QUOTES identifiers — it must match the
	/// PascalCase JSON keys the shared <c>SQuiLJson</c> serializer emits, since PG folds unquoted
	/// identifiers to lowercase and would otherwise fail to bind the recordset columns.
	/// <para>
	/// Binary columns: the shared <c>SQuiLBinaryJsonConverter</c> (used by <c>SQuiLJson.Serialize</c>,
	/// which <c>PostgresDataContext.AddJsonParameter</c> calls) serialises <see cref="byte"/>[] as
	/// bare uppercase hex, so the column is declared <c>text</c> in the AS-list and decoded back to
	/// <c>bytea</c> with <c>decode(x."Col", 'hex')</c> — the PG mirror of SQL Server's
	/// <c>Convert(varbinary(N), …, 2)</c> and SQLite's <c>unhex(…)</c>.
	/// </para>
	/// </summary>
	public string ShredStatement(SQuiL.SourceGenerator.Parser.CodeBlock block)
	{
		var name = block.TempTableName ?? block.Name;
		var cols = block.Properties;

		var insertList = string.Join(", ", cols.Select(p => p.Identifier.Value));       // bare (Option B)
		var selectList = string.Join(", ", cols.Select(SelectColumn));                   // x."Col" / decode(x."Col",'hex')
		var recordList = string.Join($",{'\n'}\t", cols.Select(RecordColumn));           // "Col" <pgtype>  (quoted)

		return $"""
			Insert Into {name}({insertList})
			Select {selectList}
			From json_to_recordset({ShredParamName(block)}) AS x(
				{recordList});
			""".Replace("\r\n", "\n");

		static string SelectColumn(SQuiL.SourceGenerator.Parser.CodeItem p)
			=> IsBinary(p)
				? $"decode(x.\"{p.Identifier.Value}\", 'hex')"
				: $"x.\"{p.Identifier.Value}\"";

		static string RecordColumn(SQuiL.SourceGenerator.Parser.CodeItem p)
			=> IsBinary(p)
				? $"\"{p.Identifier.Value}\" text"                      // hex string arrives as text
				: $"\"{p.Identifier.Value}\" {p.Type.Original}";        // author's PG type verbatim

		static bool IsBinary(SQuiL.SourceGenerator.Parser.CodeItem p)
			=> p.Type.Type is SQuiL.Tokenizer.TokenType.TYPE_VARBINARY
				or SQuiL.Tokenizer.TokenType.TYPE_BINARY
				or SQuiL.Tokenizer.TokenType.TYPE_IMAGE;
	}
}
