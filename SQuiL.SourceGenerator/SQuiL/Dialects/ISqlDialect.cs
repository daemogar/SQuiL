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
