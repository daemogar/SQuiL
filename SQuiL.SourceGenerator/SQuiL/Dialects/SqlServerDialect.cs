using System.Linq;

namespace SQuiL.Dialects;

/// <summary>
/// The SQL Server dialect: the single source of every SQL-Server-specific string the
/// generator bakes into emitted C# and SQL. Phase 1 has one concrete dialect; Phase 2
/// extracts an <c>ISqlDialect</c> interface from this surface and adds SQLite.
/// </summary>
public class SqlServerDialect
{
	/// <summary>The provider using-directive emitted into every generated data-context file.</summary>
	public string ProviderUsingDirective() => "using Microsoft.Data.SqlClient;";

	/// <summary>The provider exception type caught in the generated execute/read try-blocks.</summary>
	public string ProviderExceptionType() => "SqlException";

	/// <summary>The T-SQL database directive that scopes the query to a catalog.</summary>
	public string DatabaseDirective(string catalog) => $"Use [{catalog}];";

	/// <summary>
	/// The T-SQL table-variable declaration for an input/output table block, e.g.
	/// <c>Declare @Params_X table([Col] int Null, ...);</c>. <paramref name="newLine"/> is the
	/// emitter's active newline so the multi-column layout matches the surrounding output exactly.
	/// </summary>
	public string TableVariableDeclaration(SQuiL.SourceGenerator.Parser.CodeBlock block, string newLine)
		=> $"""
			Declare {block.DatabaseType.Original}(
				{string.Join($",{newLine}\t", block.Properties.Select(p
					=> $"[{p.Identifier.Value}] {p.Type.Original}{(p.IsNullable ? " Null" : "")}"))});
			""";

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a column (delegates to the type map).</summary>
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem item) => item.DataReader();

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a scalar block (delegates to the type map).</summary>
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeBlock block) => block.DataReader();

	/// <summary>The <c>System.Data.SqlDbType.*</c> parameter-type expression for a block (delegates to the type map).</summary>
	public string ParamTypeExpr(SQuiL.SourceGenerator.Parser.CodeBlock block) => block.SqlDbType();

	/// <summary>The fully-qualified variable-length-string parameter type (used for @EnvironmentName).</summary>
	public string VarCharType() => "System.Data.SqlDbType.VarChar";

	/// <summary>The fully-qualified boolean parameter type (used for @Debug / @SuppressDebug).</summary>
	public string BitType() => "System.Data.SqlDbType.Bit";
}
