using System.Collections.Generic;
using System.Linq;

namespace SQuiL.Dialects;

/// <summary>
/// The SQLite dialect: the source of every SQLite-specific string the generator bakes into
/// emitted C# and SQL. Type-map, temp-table, and shred members are implemented in Phase 3B
/// Tasks 4-6; they throw until then so the registry compiles from first registration.
/// </summary>
public class SqliteDialect : ISqlDialect
{
	public IEnumerable<string> UsingDirectives() => ["using Microsoft.Data.Sqlite;"];
	public string ProviderExceptionType() => "SqliteException";
	public string RuntimeBaseType() => "SqliteDataContext";
	public string DatabaseDirective(string catalog) => "";
	public string VarCharType() => "Microsoft.Data.Sqlite.SqliteType.Text";
	public string BitType() => "Microsoft.Data.Sqlite.SqliteType.Integer";

	/// <summary>
	/// The native SQLite temp-table declaration for an input/output table/object block, e.g.
	/// <c>Drop Table If Exists Returns_Person; Create Temp Table Returns_Person (PersonID INTEGER Not Null, ...);</c>.
	/// The table is created under its ORIGINAL (unstripped) name (see
	/// <see cref="SQuiL.SourceGenerator.Parser.CodeBlock.SqliteTableName"/>) so it matches the
	/// verbatim body, which references the temp table by that full name.
	/// The leading <c>Drop Table If Exists</c> makes re-running the same connection/session safe
	/// (SQLite temp tables otherwise persist for the life of the connection, so a second
	/// execution would fail with "table already exists"). <c>Primary Key</c>/<c>Not Null</c> are
	/// native SQLite column constraints — no bracket-quoting is required (SQLite does not use
	/// T-SQL's <c>[...]</c> delimited-identifier syntax), so column names are emitted bare.
	/// </summary>
	public string TableVariableDeclaration(SQuiL.SourceGenerator.Parser.CodeBlock block, string newLine)
	{
		var name = block.SqliteTableName ?? block.Name;
		var cols = string.Join($",{newLine}\t", block.Properties.Select(p
			=> $"{p.Identifier.Value} {p.Type.Original}{(p.IsNullable ? "" : " Not Null")}{(p.IsPrimaryKey ? " Primary Key" : "")}"));

		return $"""
			Drop Table If Exists {name};
			Create Temp Table {name} (
				{cols});
			""".Replace("\r\n", "\n");
	}

	/// <summary>
	/// The native SQLite scalar declaration. SQLite has no bare scalar-variable syntax (no T-SQL
	/// <c>Declare @x int;</c>), so a scalar is reconstructed as a single-column
	/// <c>Create Temp Table</c> — the inverse of <c>SQuiLParser</c>'s single-column-object collapse.
	/// Uses <see cref="SQuiL.SourceGenerator.Parser.CodeBlock.SqliteScalarTableName"/> /
	/// <see cref="SQuiL.SourceGenerator.Parser.CodeBlock.SqliteScalarColumn"/> (populated by the
	/// collapse branch) to regenerate a physically-matching statement; falls back to the block's
	/// own name/type if those are absent.
	/// </summary>
	public string ScalarVariableDeclaration(SQuiL.SourceGenerator.Parser.CodeBlock block, string newLine)
	{
		var name = block.SqliteScalarTableName ?? block.Name;
		var col = block.SqliteScalarColumn;
		var colDef = col is not null
			? $"{col.Identifier.Value} {col.Type.Original}{(col.IsNullable ? "" : " Not Null")}{(col.IsPrimaryKey ? " Primary Key" : "")}"
			: $"{block.Name} {block.DatabaseType.Original}";

		return $"""
			Drop Table If Exists {name};
			Create Temp Table {name} (
				{colDef});
			""".Replace("\r\n", "\n");
	}

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a table/object column. Computed
	/// directly from the token type — does NOT delegate to <c>Token.DataReader()</c>, which is
	/// SQL-Server-shaped (e.g. <c>GetGuid</c>, unavailable on Microsoft.Data.Sqlite's reader).</summary>
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem item)
		=> "reader." + SqliteReader(item.Type.Type);

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a scalar block. See <see cref="ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem)"/>.</summary>
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> "reader." + SqliteReader(block.DatabaseType.Type);

	static string SqliteReader(SQuiL.Tokenizer.TokenType type) => type switch
	{
		SQuiL.Tokenizer.TokenType.TYPE_BIGINT => "GetInt64",
		SQuiL.Tokenizer.TokenType.TYPE_STRING => "GetString",
		SQuiL.Tokenizer.TokenType.TYPE_DOUBLE => "GetDouble",
		SQuiL.Tokenizer.TokenType.TYPE_VARBINARY => "GetFieldValue<byte[]>",
		SQuiL.Tokenizer.TokenType.TYPE_DECIMAL => "GetDecimal",
		SQuiL.Tokenizer.TokenType.TYPE_BOOLEAN => "GetBoolean",
		SQuiL.Tokenizer.TokenType.TYPE_DATETIME => "GetFieldValue<System.DateTime>",
		SQuiL.Tokenizer.TokenType.TYPE_GUID => "GetFieldValue<System.Guid>",
		_ => "GetValue",
	};

	/// <summary>The <c>Microsoft.Data.Sqlite.SqliteType.*</c> parameter-type expression for a
	/// block, computed directly from the token type (SQLite has only four bind types).</summary>
	public string ParamTypeExpr(SQuiL.SourceGenerator.Parser.CodeBlock block) => SqliteParam(block.DatabaseType.Type);

	static string SqliteParam(SQuiL.Tokenizer.TokenType type) => "Microsoft.Data.Sqlite.SqliteType." + (type switch
	{
		SQuiL.Tokenizer.TokenType.TYPE_BIGINT or SQuiL.Tokenizer.TokenType.TYPE_BOOLEAN => "Integer",
		SQuiL.Tokenizer.TokenType.TYPE_DOUBLE => "Real",
		SQuiL.Tokenizer.TokenType.TYPE_VARBINARY => "Blob",
		_ => "Text", // TEXT, DECIMAL, DATETIME, GUID all bind as TEXT
	});

	/// <summary>
	/// Returns the JSON parameter name for the given input block:
	/// <c>@__json_Params_&lt;Name&gt;</c> for a table, <c>@__json_Param_&lt;Name&gt;</c> for an object.
	/// Identical shape to <c>SqlServerDialect.ShredParamName</c> so the emitter's call sites
	/// (see <c>SQuiLDataContext.InsertQueries</c>) stay dialect-agnostic.
	/// </summary>
	public string ShredParamName(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> $"@__json_Param{(block.IsTable ? "s" : "")}_{block.Name}";

	/// <summary>
	/// Builds the SQLite <c>Insert Into … Select json_extract(…) … From json_each(@__json_…);</c>
	/// shred — the SQLite analogue of SQL Server's <c>OpenJson … With (…)</c> shred. The insert
	/// target is the bare temp-table name (SQLite creates the table under its full
	/// <c>Params_</c>/<c>Param_</c> name in <see cref="TableVariableDeclaration"/>, unlike T-SQL's
	/// <c>@</c>-prefixed table variable). Each column is pulled from the JSON element with
	/// <c>json_extract(value, '$.Col')</c>.
	/// <para>
	/// Binary (BLOB) columns: the shared <c>SQuiLBinaryJsonConverter</c> (used by
	/// <c>SQuiLJson.Serialize</c>, which <c>SqliteDataContext.AddJsonParameter</c> calls) already
	/// serialises <see cref="byte"/>[] as bare uppercase hex, so the extracted hex string is
	/// decoded back to a blob with SQLite's <c>unhex()</c> (available since SQLite 3.41; the
	/// Microsoft.Data.Sqlite 10.0.10 bundle ships SQLite 3.49.1). This mirrors the SQL Server
	/// side's <c>Convert(varbinary(N), …, 2)</c> and keeps the JSON payload identical across both
	/// dialects — see the Task 9 <c>SqliteBlobRoundTripTests</c> for the runtime round-trip.
	/// </para>
	/// </summary>
	public string ShredStatement(SQuiL.SourceGenerator.Parser.CodeBlock block)
	{
		// Insert target = the ORIGINAL (unstripped) temp-table name the create used
		// (see TableVariableDeclaration / CodeBlock.SqliteTableName), e.g. "Params_Person",
		// NOT the stripped base Name ("Person") — the two must match or the insert misses
		// the table. Falls back to Name if SqliteTableName is unset (defensive).
		var name = block.SqliteTableName ?? block.Name;
		var cols = block.Properties;
		var insertList = string.Join(", ", cols.Select(p => $"[{p.Identifier.Value}]"));
		var selectList = string.Join(", ", cols.Select(SelectColumn));

		// Normalize to \n so `writer.Block` (which splits on \n) strips the raw literal
		// cleanly on every platform — matches the SqlServerDialect shred's rationale.
		return $"""
			Insert Into {name}({insertList})
			Select {selectList}
			From json_each({ShredParamName(block)});
			""".Replace("\r\n", "\n");

		static string SelectColumn(SQuiL.SourceGenerator.Parser.CodeItem p)
			=> IsBinary(p)
				? $"unhex(json_extract(value, '$.{p.Identifier.Value}'))"
				: $"json_extract(value, '$.{p.Identifier.Value}')";

		static bool IsBinary(SQuiL.SourceGenerator.Parser.CodeItem p)
			=> p.Type.Type is SQuiL.Tokenizer.TokenType.TYPE_VARBINARY
				or SQuiL.Tokenizer.TokenType.TYPE_BINARY
				or SQuiL.Tokenizer.TokenType.TYPE_IMAGE;
	}
}
