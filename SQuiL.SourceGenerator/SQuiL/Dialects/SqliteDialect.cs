using System;
using System.Collections.Generic;

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

	public string TableVariableDeclaration(SQuiL.SourceGenerator.Parser.CodeBlock block, string newLine)
		=> throw new NotImplementedException("SqliteDialect.TableVariableDeclaration — Task 5");

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

	public string ShredParamName(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new NotImplementedException("SqliteDialect.ShredParamName — Task 6");
	public string ShredStatement(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new NotImplementedException("SqliteDialect.ShredStatement — Task 6");
}
