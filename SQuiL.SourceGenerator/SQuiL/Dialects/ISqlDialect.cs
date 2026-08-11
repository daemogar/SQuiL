using System.Collections.Generic;

using SQuiL.SourceGenerator.Parser;

namespace SQuiL.Dialects;

/// <summary>
/// The generator-internal dialect abstraction: the single source of every provider-specific
/// string the source generator bakes into emitted C# and SQL. <see cref="SqlServerDialect"/> is
/// the only implementation today; Phase 3 adds SQLite (and later PostgreSQL). Extracted from the
/// concrete SQL Server dialect in Phase 2A — signatures are not frozen and may be reshaped when a
/// second dialect reveals a concrete need.
/// </summary>
public interface ISqlDialect
{
	/// <summary>The provider using-directives emitted into every generated data-context file (one or more).</summary>
	IEnumerable<string> UsingDirectives();

	/// <summary>The provider exception type caught in the generated execute/read try-blocks.</summary>
	string ProviderExceptionType();

	/// <summary>The database directive that scopes the query to a catalog (empty when the dialect has none).</summary>
	string DatabaseDirective(string catalog);

	/// <summary>The table-variable declaration for an input/output table block.</summary>
	string TableVariableDeclaration(CodeBlock block, string newLine);

	/// <summary>
	/// The scalar output-variable declaration for an <c>OUTPUT_VARIABLE</c> block (e.g. T-SQL
	/// <c>Declare @Return_Total int;</c>). SQL Server declares a bare scalar variable directly.
	/// SQLite has no such syntax — a SQLite scalar is really a collapsed single-column
	/// <c>Create Temp Table</c> declaration (see <c>SQuiLParser</c>'s Sqlite collapse branch), so
	/// <see cref="SqliteDialect"/> reconstructs that statement instead.
	/// </summary>
	string ScalarVariableDeclaration(CodeBlock block, string newLine);

	/// <summary>
	/// Rewrites the author's query body before it is baked into the generated command text.
	/// SQL Server appends <c>As &lt;Name&gt;</c> to a bare single-scalar <c>Select @Return_X</c> so the
	/// result set carries a column name the runtime shape key can route (see
	/// <see cref="ScalarSelectAliaser"/>). Temp-table-header dialects (SQLite, PostgreSQL) return
	/// the body unchanged — their scalars select a real column, which is already named.
	/// </summary>
	/// <param name="body">The author's query body, verbatim.</param>
	/// <param name="outputs">Every <c>OUTPUT</c> block declared by the file.</param>
	string RewriteOutputSelects(string body, IEnumerable<CodeBlock> outputs);

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a column.</summary>
	string ReaderAccessor(CodeItem item);

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a scalar block.</summary>
	string ReaderAccessor(CodeBlock block);

	/// <summary>The parameter-type expression for a block.</summary>
	string ParamTypeExpr(CodeBlock block);

	/// <summary>The variable-length-string parameter type (used for @EnvironmentName).</summary>
	string VarCharType();

	/// <summary>The boolean parameter type (used for @Debug / @SuppressDebug).</summary>
	string BitType();

	/// <summary>The JSON parameter name for an input block.</summary>
	string ShredParamName(CodeBlock block);

	/// <summary>The input-shred statement for an input block.</summary>
	string ShredStatement(CodeBlock block);

	/// <summary>The runtime provider base-class name generated data contexts inherit (e.g. <c>SqlServerDataContext</c>).</summary>
	string RuntimeBaseType();
}
