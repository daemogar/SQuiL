using System.Collections.Generic;

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
		=> throw new System.NotImplementedException();
	public string ScalarVariableDeclaration(SQuiL.SourceGenerator.Parser.CodeBlock block, string newLine)
		=> throw new System.NotImplementedException();
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem item)
		=> throw new System.NotImplementedException();
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new System.NotImplementedException();
	public string ParamTypeExpr(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new System.NotImplementedException();
	public string ShredParamName(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new System.NotImplementedException();
	public string ShredStatement(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> throw new System.NotImplementedException();
}
