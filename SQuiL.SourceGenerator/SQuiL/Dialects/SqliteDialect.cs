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
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem item)
		=> throw new NotImplementedException("SqliteDialect.ReaderAccessor(item) — Task 4");
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new NotImplementedException("SqliteDialect.ReaderAccessor(block) — Task 4");
	public string ParamTypeExpr(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new NotImplementedException("SqliteDialect.ParamTypeExpr — Task 4");
	public string ShredParamName(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new NotImplementedException("SqliteDialect.ShredParamName — Task 6");
	public string ShredStatement(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new NotImplementedException("SqliteDialect.ShredStatement — Task 6");
}
